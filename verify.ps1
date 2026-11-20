param(
    [switch]$VerboseSmoke,
    [switch]$CheckIssues,
    [int]$IssueCheckDepth = 20
)

$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot
$csproj = Join-Path $projectRoot 'ACT Spike.csproj'
$godotConsole = if ($env:GODOT_CONSOLE) { $env:GODOT_CONSOLE } else { 'D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe' }
$smokeLog = Join-Path $projectRoot '.godot\headless-smoke.log'

Write-Host '[Verify] Building C# project (warnings as errors)...'
& dotnet build $csproj --no-restore -warnaserror -nologo -v minimal
if ($LASTEXITCODE -ne 0) {
    Write-Error "[Verify] FAIL: dotnet build exited with code $LASTEXITCODE"
    exit $LASTEXITCODE
}

if (-not (Test-Path $godotConsole)) {
    Write-Error "[Verify] FAIL: Godot console binary not found at $godotConsole (override with GODOT_CONSOLE)"
    exit 1
}

if (Test-Path $smokeLog) {
    Remove-Item $smokeLog -Force
}

Write-Host '[Verify] Running headless ACT_SMOKE=1 (--quit-after 3600)...'
$prevSmoke = $env:ACT_SMOKE
$capturedOutput = $null
try {
    $env:ACT_SMOKE = '1'
    if ($VerboseSmoke) {
        & $godotConsole --headless --path $projectRoot --log-file $smokeLog --quit-after 3600
    }
    else {
        $capturedOutput = & $godotConsole --headless --path $projectRoot --log-file $smokeLog --quit-after 3600 2>&1
    }
    $godotExit = $LASTEXITCODE
}
finally {
    if ($null -eq $prevSmoke) { Remove-Item Env:ACT_SMOKE -ErrorAction SilentlyContinue }
    else { $env:ACT_SMOKE = $prevSmoke }
}

if ($godotExit -ne 0) {
    if (-not $VerboseSmoke) {
        if (Test-Path $smokeLog) {
            Get-Content $smokeLog | Where-Object { $_ -match '^\[Smoke\]' } | ForEach-Object { Write-Host $_ }
            Write-Host "[Verify] Full smoke log: $smokeLog"
        }
        elseif ($capturedOutput) {
            $capturedOutput | ForEach-Object { Write-Host $_ }
        }
    }
    Write-Error "[Verify] FAIL: Godot headless smoke exited with code $godotExit"
    exit $godotExit
}

if (-not (Test-Path $smokeLog)) {
    Write-Error "[Verify] FAIL: Smoke log missing at $smokeLog"
    exit 1
}

$logLines = Get-Content $smokeLog
$logContent = $logLines -join "`n"
$hasAllPass = $logContent -match '\[Smoke\] ALL PASS'
$hasResetPass = $logContent -match '\[Smoke\] PASS R reset'
$hasFail = $logContent -match '\[Smoke\] FAIL'

if (-not $VerboseSmoke) {
    $logLines | Where-Object { $_ -match '^\[Smoke\] (PASS|FAIL|ALL PASS|DONE)' } | ForEach-Object { Write-Host $_ }
    Write-Host "[Verify] Full smoke log saved to $smokeLog"
}

if (-not $hasAllPass -or -not $hasResetPass -or $hasFail) {
    Write-Error "[Verify] FAIL: Smoke contract incomplete (ALL PASS=$hasAllPass, R reset=$hasResetPass, FAIL=$hasFail). Inspect $smokeLog"
    exit 1
}

if ($CheckIssues) {
    Write-Host "[Verify] Checking last $IssueCheckDepth commits for unclosed GitHub child tickets..."
    $subjects = & git -C $projectRoot log -n $IssueCheckDepth --pretty=%s 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Error '[Verify] FAIL: unable to read git log for -CheckIssues'
        exit 1
    }

    $referenced = New-Object 'System.Collections.Generic.HashSet[int]'
    foreach ($subject in $subjects) {
        foreach ($m in [regex]::Matches($subject, '\(#(\d+)\)')) {
            [void]$referenced.Add([int]$m.Groups[1].Value)
        }
    }

    if ($referenced.Count -gt 0) {
        $openJson = & gh issue list --state open --limit 100 --json number,title,body 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Error "[Verify] FAIL: gh issue list failed during -CheckIssues: $openJson"
            exit 1
        }

        $openIssues = $openJson | ConvertFrom-Json
        $unclosedTickets = @()
        foreach ($issue in $openIssues) {
            $num = [int]$issue.number
            if (-not $referenced.Contains($num)) { continue }

            $isChildTicket = ($issue.body -match '(?m)^##\s*Parent\b') -or ($issue.body -match '(?m)^Part of #\d+')
            if ($isChildTicket) {
                $unclosedTickets += $issue
            }
            else {
                Write-Host "[Verify] INFO: referenced parent/spec issue #$num remains OPEN ($($issue.title))"
            }
        }

        if ($unclosedTickets.Count -gt 0) {
            foreach ($ticket in $unclosedTickets) {
                Write-Host "[Verify] UNCLOSED TICKET: #$($ticket.number) $($ticket.title)"
            }
            Write-Error '[Verify] FAIL: one or more commit-referenced child tickets are still OPEN on GitHub.'
            exit 1
        }
        Write-Host '[Verify] PASS: all commit-referenced child tickets are closed on GitHub.'
    }
}

Write-Host '[Verify] PASS: clean 0-warning build + full headless smoke (ALL PASS & R reset).'
exit 0
