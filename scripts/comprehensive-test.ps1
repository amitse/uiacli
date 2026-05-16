#!/usr/bin/env pwsh
# Comprehensive CLI-First Test Suite
# ALL tests go through the CLI binary - testing what agents actually use.
# Requires: Calculator open. Server running on port 9721.

$ErrorActionPreference = "Continue"
$script:passed = 0
$script:failed = 0
$script:results = @()
$script:exe = "E:\uiacli\uia.exe"

function Test($id, $category, $name, $scriptBlock) {
    try {
        $result = & $scriptBlock
        if ($result -eq $true) {
            Write-Host "  PASS [$id] $name" -ForegroundColor Green
            $script:passed++
            $script:results += [PSCustomObject]@{id=$id; status="PASS"; detail=""}
        } else {
            Write-Host "  FAIL [$id] $name -> $result" -ForegroundColor Red
            $script:failed++
            $script:results += [PSCustomObject]@{id=$id; status="FAIL"; detail="$result"}
        }
    } catch {
        Write-Host "  FAIL [$id] $name -> $_" -ForegroundColor Red
        $script:failed++
        $script:results += [PSCustomObject]@{id=$id; status="FAIL"; detail="$_"}
    }
}

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "   UIA CLI - CLI-First Test Suite" -ForegroundColor Cyan
Write-Host "   Every test goes through the CLI binary" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Ensure Calculator is open
if (-not (Get-Process -Name "CalculatorApp" -ErrorAction SilentlyContinue)) {
    Start-Process calc.exe; Start-Sleep -Seconds 2
}

# ==================== HELP ====================
Write-Host "[HELP - self-documenting]" -ForegroundColor Yellow

Test "help-01" "help" "Root --help shows description, commands, exit codes" {
    $h = & E:\uiacli\uia.exe --help 2>$null
    $joined = $h -join "`n"
    $joined -match "Windows UI Automation" -and $joined -match "uia windows" -and $joined -match "Exit codes"
}

Test "help-02" "help" "click --help shows options and window flag" {
    $h = & E:\uiacli\uia.exe click --help 2>$null
    $joined = $h -join "`n"
    $joined -match "uia click" -and $joined -match "--window" -and $joined -match "--name"
}

Test "help-03" "help" "batch --help shows JSON format and verbose" {
    $h = & E:\uiacli\uia.exe batch --help 2>$null
    $joined = $h -join "`n"
    $joined -match "uia batch" -and $joined -match "--verbose" -and $joined -match "onError"
}

Test "help-04" "help" "key --help shows supported keys list" {
    $h = & E:\uiacli\uia.exe key --help 2>$null
    $joined = $h -join "`n"
    $joined -match "ctrl" -and $joined -match "alt" -and $joined -match "f1-f12" -and $joined -match "enter"
}

Test "help-05" "help" "find --help shows filter options" {
    $h = & E:\uiacli\uia.exe find --help 2>$null
    $joined = $h -join "`n"
    $joined -match "--name" -and $joined -match "--type" -and $joined -match "--id"
}

# ==================== SERVER ====================
Write-Host "[SERVER - auto-start, lifecycle]" -ForegroundColor Yellow

Test "srv-01" "server" "Windows command auto-starts server if not running" {
    $output = & E:\uiacli\uia.exe windows 2>$null
    $w = $output | ConvertFrom-Json
    $w.Count -gt 0
}

Test "srv-02" "server" "State returns complete desktop snapshot" {
    $output = & E:\uiacli\uia.exe state 2>$null
    $s = $output | ConvertFrom-Json
    $s.windows.Count -gt 0 -and $null -ne $s.cursorPosition -and $s.screens.Count -gt 0
}

# ==================== WINDOWS ====================
Write-Host "[WINDOWS - list, focus, errors]" -ForegroundColor Yellow

Test "win-01" "windows" "List windows returns array with handles and titles" {
    $output = & E:\uiacli\uia.exe windows 2>$null
    $w = $output | ConvertFrom-Json
    $w.Count -gt 0 -and $null -ne $w[0].handle -and $null -ne $w[0].title
}

Test "win-02" "windows" "Focus Calculator by title substring" {
    $output = (& E:\uiacli\uia.exe focus Calculator 2>$null) -join ""
    $r = $output | ConvertFrom-Json
    $r.ok -eq $true
}

Test "win-03" "windows" "Focus nonexistent window - WINDOW_NOT_FOUND + hint" {
    $output = & E:\uiacli\uia.exe focus ZZZNonExistent999 2>$null
    $r = $output | ConvertFrom-Json
    $r.ok -eq $false -and $r.error.code -eq "WINDOW_NOT_FOUND" -and $r.error.hint -match "windows"
}

Test "win-04" "windows" "Focused element returns valid element" {
    $output = & E:\uiacli\uia.exe focused 2>$null
    $r = $output | ConvertFrom-Json
    $null -ne $r.controlType
}

# ==================== TREE and FIND ====================
Write-Host "[ELEMENTS - tree, find, errors]" -ForegroundColor Yellow

Test "elem-01" "elements" "Tree Calculator returns nested tree with children" {
    $output = & E:\uiacli\uia.exe tree Calculator 2>$null
    $t = $output | ConvertFrom-Json
    $t.name -eq "Calculator" -and $t.controlType -eq "Window" -and $t.children.Count -gt 0
}

Test "elem-02" "elements" "Tree --depth 1 has children but no grandchildren" {
    $output = & E:\uiacli\uia.exe tree Calculator --depth 1 2>$null
    $t = $output | ConvertFrom-Json
    $hasKids = $t.children.Count -gt 0
    $noGrandkids = -not ($t.children | Where-Object { $_.children -and $_.children.Count -gt 0 })
    $hasKids -and $noGrandkids
}

Test "elem-03" "elements" "Tree nonexistent window - WINDOW_NOT_FOUND" {
    $output = & E:\uiacli\uia.exe tree FakeApp123 2>$null
    $r = $output | ConvertFrom-Json
    $r.ok -eq $false -and $r.error.code -eq "WINDOW_NOT_FOUND"
}

Test "elem-04" "elements" "Find by --name Equals returns 1 result with automationId" {
    $output = & E:\uiacli\uia.exe find Calculator --name Equals --type Button 2>$null
    $r = $output | ConvertFrom-Json
    $r.count -eq 1 -and $r.elements[0].automationId -eq "equalButton"
}

Test "elem-05" "elements" "Find by --type Button returns many results" {
    $output = & E:\uiacli\uia.exe find Calculator --type Button 2>$null
    $r = $output | ConvertFrom-Json
    $r.count -gt 5
}

Test "elem-06" "elements" "Find by --id num5Button returns Five" {
    $output = & E:\uiacli\uia.exe find Calculator --id num5Button 2>$null
    $r = $output | ConvertFrom-Json
    $r.count -eq 1 -and $r.elements[0].name -eq "Five"
}

Test "elem-07" "elements" "Find with NO filters - NO_FILTER error + hint" {
    $output = & E:\uiacli\uia.exe find Calculator 2>$null
    $r = $output | ConvertFrom-Json
    $r.ok -eq $false -and $r.error.code -eq "NO_FILTER" -and $r.error.hint -match "--name"
}

# ==================== CLICK ====================
Write-Host "[CLICK - coordinates, elements, errors]" -ForegroundColor Yellow

Test "click-01" "click" "Click NO args - NO_TARGET error + hint with examples" {
    $output = & E:\uiacli\uia.exe click 2>$null
    $r = $output | ConvertFrom-Json
    $r.ok -eq $false -and $r.error.code -eq "NO_TARGET" -and $r.error.hint -match "uia click"
}

Test "click-02" "click" "Click --name without --window - NO_WINDOW error + hint" {
    $output = & E:\uiacli\uia.exe click --name Five 2>$null
    $r = $output | ConvertFrom-Json
    $r.ok -eq $false -and $r.error.code -eq "NO_WINDOW" -and $r.error.hint -match "--window"
}

Test "click-03" "click" "Click --window FakeApp - WINDOW_NOT_FOUND" {
    $output = & E:\uiacli\uia.exe click --window FakeApp999 --name OK 2>$null
    $r = $output | ConvertFrom-Json
    $r.ok -eq $false -and $r.error.code -eq "WINDOW_NOT_FOUND"
}

Test "click-04" "click" "Click Calculator Five button by name succeeds" {
    $output = & E:\uiacli\uia.exe click --window Calculator --name Five --type Button 2>$null
    $r = $output | ConvertFrom-Json
    $r.ok -eq $true
}

Test "click-05" "click" "Click nonexistent element - ELEMENT_NOT_FOUND" {
    $output = & E:\uiacli\uia.exe click --window Calculator --name Fivee 2>$null
    $r = $output | ConvertFrom-Json
    $r.ok -eq $false -and $r.error.code -eq "ELEMENT_NOT_FOUND"
}

Test "click-06" "click" "Click by --x --y coordinates" {
    $output = & E:\uiacli\uia.exe click --x 500 --y 500 2>$null
    $r = $output | ConvertFrom-Json
    $r.ok -eq $true
}

# ==================== TYPE ====================
Write-Host "[TYPE - text input]" -ForegroundColor Yellow

Test "type-01" "type" "Type text succeeds" {
    $output = & E:\uiacli\uia.exe type hello 2>$null
    $r = $output | ConvertFrom-Json
    $r.ok -eq $true
}

# ==================== KEY ====================
Write-Host "[KEY - key combos]" -ForegroundColor Yellow

Test "key-01" "key" "Key enter succeeds" {
    $output = & E:\uiacli\uia.exe key enter 2>$null
    $r = $output | ConvertFrom-Json
    $r.ok -eq $true
}

Test "key-02" "key" "Key ctrl+a succeeds" {
    $output = & E:\uiacli\uia.exe key ctrl+a 2>$null
    $r = $output | ConvertFrom-Json
    $r.ok -eq $true
}

# ==================== BATCH ====================
Write-Host "[BATCH - execution, errors, verbose]" -ForegroundColor Yellow

Test "batch-01" "batch" "Batch nonexistent window - WINDOW_NOT_FOUND at batch level" {
    $json = '{"actions":[{"type":"click","element":{"name":"OK"}}],"window":"FakeApp999"}'
    $f = [System.IO.Path]::GetTempFileName()
    Set-Content -Path $f -Value $json -NoNewline
    $output = & E:\uiacli\uia.exe batch $f 2>$null
    Remove-Item $f
    $r = $output | ConvertFrom-Json
    $r.results[0].ok -eq $false -and $r.results[0].error.code -eq "WINDOW_NOT_FOUND"
}

Test "batch-02" "batch" "Batch stop-on-error skips remaining actions" {
    $json = '{"actions":[{"type":"click","element":{"name":"Five","controlType":"Button"}},{"type":"click","element":{"name":"ZZZZZ"}},{"type":"click","element":{"name":"Two","controlType":"Button"}}],"window":"Calculator","onError":"stop"}'
    $f = [System.IO.Path]::GetTempFileName()
    Set-Content -Path $f -Value $json -NoNewline
    $output = & E:\uiacli\uia.exe batch $f 2>$null
    Remove-Item $f
    $r = $output | ConvertFrom-Json
    $r.results.Count -eq 2
}

Test "batch-03" "batch" "Batch continue-on-error executes all 3 actions" {
    $json = '{"actions":[{"type":"click","element":{"name":"Five","controlType":"Button"}},{"type":"click","element":{"name":"ZZZZZ"}},{"type":"click","element":{"name":"Two","controlType":"Button"}}],"window":"Calculator","onError":"continue"}'
    $f = [System.IO.Path]::GetTempFileName()
    Set-Content -Path $f -Value $json -NoNewline
    $output = & E:\uiacli\uia.exe batch $f 2>$null
    Remove-Item $f
    $r = $output | ConvertFrom-Json
    $r.results.Count -eq 3 -and $r.results[0].ok -and -not $r.results[1].ok -and $r.results[2].ok
}

Test "batch-04" "batch" "Batch inline wait for existing element succeeds fast" {
    $json = '{"actions":[{"type":"wait","until":{"elementExists":{"name":"Equals","controlType":"Button"}},"timeoutMs":3000}],"window":"Calculator"}'
    $f = [System.IO.Path]::GetTempFileName()
    Set-Content -Path $f -Value $json -NoNewline
    $output = & E:\uiacli\uia.exe batch $f 2>$null
    Remove-Item $f
    $r = $output | ConvertFrom-Json
    $r.results[0].ok -and $r.results[0].durationMs -lt 2000
}

Test "batch-05" "batch" "Batch inline read returns Calculator display value" {
    $json = '{"actions":[{"type":"click","element":{"name":"Clear","controlType":"Button"}},{"type":"click","element":{"name":"Four","controlType":"Button"}},{"type":"click","element":{"name":"Two","controlType":"Button"}},{"type":"read","element":{"automationId":"CalculatorResults"}}],"window":"Calculator","onError":"continue"}'
    $f = [System.IO.Path]::GetTempFileName()
    Set-Content -Path $f -Value $json -NoNewline
    $output = & E:\uiacli\uia.exe batch $f 2>$null
    Remove-Item $f
    $r = $output | ConvertFrom-Json
    $readResult = $r.results | Where-Object { $_.value } | Select-Object -Last 1
    $readResult.ok -and $readResult.value -match "42"
}

Test "batch-06" "batch" "Batch unknown action - UNKNOWN_ACTION error" {
    $json = '{"actions":[{"type":"foobar"}],"window":"Calculator"}'
    $f = [System.IO.Path]::GetTempFileName()
    Set-Content -Path $f -Value $json -NoNewline
    $output = & E:\uiacli\uia.exe batch $f 2>$null
    Remove-Item $f
    $r = $output | ConvertFrom-Json
    $r.results[0].ok -eq $false -and $r.results[0].error.code -eq "UNKNOWN_ACTION"
}

Test "batch-07" "batch" "Batch with per-action delay takes longer" {
    $json = '{"actions":[{"type":"click","element":{"name":"Five","controlType":"Button"},"delayMs":300},{"type":"click","element":{"name":"Five","controlType":"Button"}}],"window":"Calculator"}'
    $f = [System.IO.Path]::GetTempFileName()
    Set-Content -Path $f -Value $json -NoNewline
    $output = & E:\uiacli\uia.exe batch $f 2>$null
    Remove-Item $f
    $r = $output | ConvertFrom-Json
    $r.totalDurationMs -ge 300
}

Test "batch-08" "batch" "Batch empty actions array returns empty results" {
    $json = '{"actions":[],"window":"Calculator"}'
    $f = [System.IO.Path]::GetTempFileName()
    Set-Content -Path $f -Value $json -NoNewline
    $output = & E:\uiacli\uia.exe batch $f 2>$null
    Remove-Item $f
    $r = $output | ConvertFrom-Json
    $r.results.Count -eq 0 -and $r.totalDurationMs -ge 0
}

Test "batch-09" "batch" "Verbose batch (overlay) takes longer than non-verbose" {
    $json1 = '{"actions":[{"type":"click","element":{"name":"Five","controlType":"Button"}}],"window":"Calculator","verbose":false}'
    $f1 = [System.IO.Path]::GetTempFileName()
    Set-Content -Path $f1 -Value $json1 -NoNewline
    $json2 = '{"actions":[{"type":"click","element":{"name":"Five","controlType":"Button"}}],"window":"Calculator","verbose":true}'
    $f2 = [System.IO.Path]::GetTempFileName()
    Set-Content -Path $f2 -Value $json2 -NoNewline
    $output1 = & E:\uiacli\uia.exe batch $f1 2>$null
    $fast = $output1 | ConvertFrom-Json
    $output2 = & E:\uiacli\uia.exe batch $f2 2>$null
    $slow = $output2 | ConvertFrom-Json
    Remove-Item $f1,$f2
    $slow.totalDurationMs -gt $fast.totalDurationMs
}

Test "batch-10" "batch" "North-star: Calculator 25x25=625" {
    $json = '{"actions":[{"type":"click","element":{"name":"Clear","controlType":"Button"}},{"type":"click","element":{"name":"Two","controlType":"Button"}},{"type":"click","element":{"name":"Five","controlType":"Button"}},{"type":"click","element":{"name":"Multiply by","controlType":"Button"}},{"type":"click","element":{"name":"Two","controlType":"Button"}},{"type":"click","element":{"name":"Five","controlType":"Button"}},{"type":"click","element":{"name":"Equals","controlType":"Button"}},{"type":"read","element":{"automationId":"CalculatorResults"}}],"window":"Calculator","onError":"continue"}'
    $f = [System.IO.Path]::GetTempFileName()
    Set-Content -Path $f -Value $json -NoNewline
    $output = & E:\uiacli\uia.exe batch $f 2>$null
    Remove-Item $f
    $r = $output | ConvertFrom-Json
    $answer = ($r.results | Where-Object { $_.value } | Select-Object -Last 1).value
    $answer -match "625"
}

# ==================== SCREENSHOT ====================
Write-Host "[SCREENSHOT]" -ForegroundColor Yellow

Test "ss-01" "screenshot" "Screenshot Calculator returns file that exists" {
    $output = & E:\uiacli\uia.exe screenshot Calculator 2>$null
    $r = $output | ConvertFrom-Json
    $r.filePath -and (Test-Path $r.filePath) -and $r.width -gt 0
}

Test "ss-02" "screenshot" "Screenshot nonexistent window - WINDOW_NOT_FOUND" {
    $output = & E:\uiacli\uia.exe screenshot FakeApp999 2>$null
    $r = $output | ConvertFrom-Json
    $r.ok -eq $false -and $r.error.code -eq "WINDOW_NOT_FOUND"
}

# ==================== WAIT ====================
Write-Host "[WAIT - conditions, timeout]" -ForegroundColor Yellow

Test "wait-01" "wait" "Wait NO condition - NO_CONDITION error + hint" {
    $output = & E:\uiacli\uia.exe wait 2>$null
    $r = $output | ConvertFrom-Json
    $r.ok -eq $false -and $r.error.code -eq "NO_CONDITION" -and $r.error.hint -match "--name"
}

Test "wait-02" "wait" "Wait for existing element succeeds" {
    $output = & E:\uiacli\uia.exe wait --window Calculator --name Equals --timeout 2000 2>$null
    $r = $output | ConvertFrom-Json
    $r.ok -eq $true
}

Test "wait-03" "wait" "Wait timeout for nonexistent element - TIMEOUT" {
    $output = & E:\uiacli\uia.exe wait --window Calculator --name ZZZFake --timeout 1000 2>$null
    $r = $output | ConvertFrom-Json
    $r.ok -eq $false -and $r.error.code -eq "TIMEOUT"
}

# ==================== PROCESS/LAUNCH ====================
Write-Host "[PROCESS/LAUNCH]" -ForegroundColor Yellow

Test "proc-01" "process" "Processes returns list with IDs" {
    $output = & E:\uiacli\uia.exe processes 2>$null
    $r = $output | ConvertFrom-Json
    $r.Count -gt 0
}

Test "launch-01" "launch" "Launch mspaint returns PID" {
    $output = (& E:\uiacli\uia.exe launch mspaint.exe 2>$null) -join ""
    $r = $output | ConvertFrom-Json
    $ok = $r.processId -gt 0
    Start-Sleep -Seconds 2
    Get-Process -Name "mspaint" -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 500
    $ok
}

# ==================== CLIPBOARD ====================
Write-Host "[CLIPBOARD]" -ForegroundColor Yellow

Test "clip-01" "clipboard" "Set then get clipboard roundtrip" {
    & E:\uiacli\uia.exe clipboard set uia-test-xyz789 2>$null | Out-Null
    Start-Sleep -Milliseconds 300
    $output = & E:\uiacli\uia.exe clipboard get 2>$null
    $r = $output | ConvertFrom-Json
    $r.text -eq "uia-test-xyz789"
}

# ==================== OVERLAY ====================
# Ensure Calculator is still open (previous tests may have affected it)
if (-not (Get-Process -Name "CalculatorApp" -ErrorAction SilentlyContinue)) {
    Start-Process calc.exe; Start-Sleep -Seconds 2
}
& E:\uiacli\uia.exe focus Calculator 2>$null | Out-Null
Start-Sleep -Milliseconds 500
Write-Host "[OVERLAY - highlight, annotate, cursor, clear]" -ForegroundColor Yellow

Test "ov-01" "overlay" "Highlight Calculator Equals button" {
    $output = & E:\uiacli\uia.exe highlight Calculator --name Equals --style action 2>$null
    $r = $output | ConvertFrom-Json
    $r.ok -eq $true -and $r.id -ne $null
}

Test "ov-02" "overlay" "Highlight NO element - NO_ELEMENT error" {
    $output = & E:\uiacli\uia.exe highlight Calculator 2>$null
    $r = $output | ConvertFrom-Json
    $r.ok -eq $false -and $r.error.code -eq "NO_ELEMENT"
}

Test "ov-03" "overlay" "Annotate near element" {
    $output = & E:\uiacli\uia.exe annotate Calculator --name Equals --text "Test annotation" --style reasoning 2>$null
    $r = $output | ConvertFrom-Json
    $r.ok -eq $true -and $r.id -ne $null
}

Test "ov-04" "overlay" "Clear overlay" {
    $output = & E:\uiacli\uia.exe overlay clear 2>$null
    $r = $output | ConvertFrom-Json
    $r.ok -eq $true
}

# ==================== EDGE CASES ====================
Write-Host "[EDGE - robustness]" -ForegroundColor Yellow

Test "edge-01" "edge" "Server still alive after all tests" {
    $output = & E:\uiacli\uia.exe windows 2>$null
    $w = $output | ConvertFrom-Json
    $w.Count -gt 0
}

# ==================== SUMMARY ====================
Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "   RESULTS" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Passed:  $($script:passed)" -ForegroundColor Green
Write-Host "  Failed:  $($script:failed)" -ForegroundColor $(if ($script:failed -gt 0) { "Red" } else { "Green" })
Write-Host "  Total:   $($script:passed + $script:failed)" -ForegroundColor White
Write-Host ""

if ($script:failed -gt 0) {
    Write-Host "Failed tests:" -ForegroundColor Red
    $script:results | Where-Object { $_.status -eq "FAIL" } | ForEach-Object {
        Write-Host "  [$($_.id)] $($_.detail)" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "SOME TESTS FAILED" -ForegroundColor Red
    exit 1
} else {
    Write-Host "ALL TESTS PASSED" -ForegroundColor Green
    exit 0
}
