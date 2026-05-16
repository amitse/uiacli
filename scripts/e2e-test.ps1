#!/usr/bin/env pwsh
# E2E Test: Calculator 25 * 25 = 625
# This script verifies the complete UIA CLI pipeline works end-to-end.

$ErrorActionPreference = "Stop"
$baseUrl = "http://localhost:9721"
$passed = 0
$failed = 0

function Test-Endpoint($name, $scriptBlock) {
    try {
        $result = & $scriptBlock
        if ($result) {
            Write-Host "  PASS: $name" -ForegroundColor Green
            $script:passed++
        } else {
            Write-Host "  FAIL: $name" -ForegroundColor Red
            $script:failed++
        }
    } catch {
        Write-Host "  FAIL: $name - $_" -ForegroundColor Red
        $script:failed++
    }
}

Write-Host "=== UIA CLI End-to-End Test ===" -ForegroundColor Cyan
Write-Host ""

# 1. Check server health
Write-Host "[1] Server Health" -ForegroundColor Yellow
Test-Endpoint "GET /health returns ok" {
    $health = curl.exe -s "$baseUrl/health" | ConvertFrom-Json
    $health.ok -eq $true
}

# 2. List windows
Write-Host "[2] Window Listing" -ForegroundColor Yellow
Test-Endpoint "GET /windows returns non-empty list" {
    $windows = curl.exe -s "$baseUrl/windows" | ConvertFrom-Json
    $windows.Count -gt 0
}

# 3. Desktop state
Write-Host "[3] Desktop State" -ForegroundColor Yellow
Test-Endpoint "GET /state returns complete snapshot" {
    $state = curl.exe -s "$baseUrl/state" | ConvertFrom-Json
    $state.windows.Count -gt 0 -and $null -ne $state.cursorPosition
}

# 4. Calculator test
Write-Host "[4] Calculator: 25 x 25 = 625" -ForegroundColor Yellow

# Ensure Calculator is running
$calcProcs = Get-Process -Name "CalculatorApp" -ErrorAction SilentlyContinue
if (-not $calcProcs) {
    Start-Process calc.exe
    Start-Sleep -Seconds 2
}

# Get Calculator handle
$windows = curl.exe -s "$baseUrl/windows" | ConvertFrom-Json
$calc = $windows | Where-Object { $_.title -eq "Calculator" } | Select-Object -First 1
if (-not $calc) {
    Write-Host "  SKIP: Calculator not found" -ForegroundColor Yellow
} else {
    $script:handle = $calc.handle

    Test-Endpoint "Focus Calculator" {
        $r = curl.exe -s -X POST "$baseUrl/windows/$($script:handle)/focus" | ConvertFrom-Json
        $r.ok -eq $true
    }

    Start-Sleep -Seconds 0.5

    Test-Endpoint "Get Calculator tree (depth 2)" {
        $tree = curl.exe -s "$baseUrl/windows/$script:handle/tree?depth=2" | ConvertFrom-Json
        $tree.name -eq "Calculator"
    }

    Test-Endpoint "Find Equals button" {
        $found = curl.exe -s "$baseUrl/windows/$script:handle/find?name=Equals&type=Button" | ConvertFrom-Json
        $found.count -eq 1
    }

    # Clear calculator first
    $clearBatch = @{
        actions = @(@{ type = "click"; element = @{ name = "Clear"; controlType = "Button" } })
        window = "Calculator"
        onError = "continue"
    } | ConvertTo-Json -Depth 5
    curl.exe -s -X POST -H "Content-Type: application/json" -d $clearBatch "$baseUrl/batch" | Out-Null
    Start-Sleep -Milliseconds 300

    # Execute 25 * 25 =
    # Build batch JSON and write to temp file (avoids PowerShell quote mangling)
    $batchJson = '{"actions":[' +
        '{"type":"click","element":{"name":"Clear","controlType":"Button"}},' +
        '{"type":"click","element":{"name":"Two","controlType":"Button"}},' +
        '{"type":"click","element":{"name":"Five","controlType":"Button"}},' +
        '{"type":"click","element":{"name":"Multiply by","controlType":"Button"}},' +
        '{"type":"click","element":{"name":"Two","controlType":"Button"}},' +
        '{"type":"click","element":{"name":"Five","controlType":"Button"}},' +
        '{"type":"click","element":{"name":"Equals","controlType":"Button"}},' +
        '{"type":"read","element":{"automationId":"CalculatorResults"}}' +
        '],"window":"Calculator","onError":"continue"}'
    $batchFile = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($batchFile, $batchJson)

    Test-Endpoint "Batch: Clear + 25 * 25 = 625 (result=625)" {
        $result = curl.exe -s -X POST -H "Content-Type: application/json" -d "@$batchFile" "$baseUrl/batch" | ConvertFrom-Json
        $readResult = $result.results | Where-Object { $_.value } | Select-Object -Last 1
        $readResult.value -match "625"
    }

    Test-Endpoint "Batch executes in under 1 second" {
        $result = curl.exe -s -X POST -H "Content-Type: application/json" -d "@$batchFile" "$baseUrl/batch" | ConvertFrom-Json
        $result.totalDurationMs -lt 1000
    }

    Remove-Item $batchFile -ErrorAction SilentlyContinue
}

# 5. Screenshot
Write-Host "[5] Screenshot" -ForegroundColor Yellow
if ($calc) {
    Test-Endpoint "Screenshot captures Calculator" {
        $h = $calc.handle.ToString()
        $ssFile = [System.IO.Path]::GetTempFileName()
        [System.IO.File]::WriteAllText($ssFile, '{"handle":' + $h + '}')
        $ssJson = curl.exe -s -X POST -H "Content-Type: application/json" -d "@$ssFile" "$baseUrl/screenshot"
        Remove-Item $ssFile -ErrorAction SilentlyContinue
        $ss = $ssJson | ConvertFrom-Json
        $ss.filePath -and (Test-Path $ss.filePath)
    }
}

# 6. Processes
Write-Host "[6] Process Management" -ForegroundColor Yellow
Test-Endpoint "GET /processes returns process list" {
    $procs = curl.exe -s "$baseUrl/processes" | ConvertFrom-Json
    $procs.Count -gt 0
}

# 7. Focused element
Write-Host "[7] Focused Element" -ForegroundColor Yellow
Test-Endpoint "GET /focused returns an element" {
    $focused = curl.exe -s "$baseUrl/focused" | ConvertFrom-Json
    $null -ne $focused.controlType
}

# Summary
Write-Host ""
Write-Host "=== Results ===" -ForegroundColor Cyan
Write-Host "  Passed: $passed" -ForegroundColor Green
Write-Host "  Failed: $failed" -ForegroundColor $(if ($failed -gt 0) { "Red" } else { "Green" })
Write-Host ""

if ($failed -gt 0) {
    Write-Host "SOME TESTS FAILED" -ForegroundColor Red
    exit 1
} else {
    Write-Host "ALL TESTS PASSED" -ForegroundColor Green
    exit 0
}


