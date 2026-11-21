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

Write-Host '[Verify] Checking mechanical static guardrails...'
$guardrailViolations = @()
$logicDirs = @('Scripts\ACT\Combat', 'Scripts\ACT\Enemy', 'Scripts\ACT\Player', 'Scripts\ACT\Core')
foreach ($relDir in $logicDirs) {
    $absDir = Join-Path $projectRoot $relDir
    if (-not (Test-Path $absDir)) { continue }
    Get-ChildItem -Path $absDir -Recurse -Filter '*.cs' | ForEach-Object {
        $file = $_
        $lineNum = 0
        foreach ($line in Get-Content $file.FullName) {
            $lineNum++
            $trimmed = $line.TrimStart()
            if ($trimmed.StartsWith('//')) { continue }
            if ($line -match 'override\s+void\s+_Draw\s*\(') {
                $relPath = $file.FullName.Substring($projectRoot.Length + 1)
                $guardrailViolations += "$relPath`:$lineNum`: _Draw() belongs in Visual/ or HUD/, not logic layer"
            }
        }
    }
}

$actRoot = Join-Path $projectRoot 'Scripts\ACT'
if (Test-Path $actRoot) {
    Get-ChildItem -Path $actRoot -Recurse -Filter '*.cs' | Where-Object {
        $_.Name -notlike '*Smoke.cs' -and $_.Name -ne 'SmokeTest.cs'
    } | ForEach-Object {
        $file = $_
        $lineNum = 0
        foreach ($line in Get-Content $file.FullName) {
            $lineNum++
            $trimmed = $line.TrimStart()
            if ($trimmed.StartsWith('//')) { continue }
            if ($line -match '\bCollision(Layer|Mask)\s*=\s*\d+') {
                $relPath = $file.FullName.Substring($projectRoot.Length + 1)
                $guardrailViolations += "$relPath`:$lineNum`: use CombatCollisionMasks instead of bare numeric collision mask"
            }
        }
    }
}

if ($guardrailViolations.Count -gt 0) {
    foreach ($v in $guardrailViolations) {
        Write-Host "[Verify] GUARDRAIL: $v"
    }
    Write-Error "[Verify] FAIL: $($guardrailViolations.Count) mechanical static guardrail violation(s) found."
    exit 1
}

$assetsFile = Join-Path $projectRoot '.godot\mono\temp\obj\project.assets.json'
if (Test-Path $assetsFile) {
    Write-Host '[Verify] Building C# project (warnings as errors)...'
    & dotnet build $csproj --no-restore -warnaserror -nologo -v minimal
}
else {
    Write-Host '[Verify] Restore cache missing (fresh worktree); building with restore (warnings as errors)...'
    & dotnet build $csproj -warnaserror -nologo -v minimal
}
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
$godotErrors = @($logLines | Where-Object { $_ -match '^(ERROR:|SCRIPT ERROR:)' })
$godotWarnings = @($logLines | Where-Object { $_ -match '^WARNING:' })

if (-not $VerboseSmoke) {
    $logLines | Where-Object { $_ -match '^\[Smoke\] (PASS|FAIL|ALL PASS|DONE)' } | ForEach-Object { Write-Host $_ }
    if ($godotWarnings.Count -gt 0) {
        Write-Host "[Verify] WARN: $($godotWarnings.Count) Godot WARNING line(s) recorded in $smokeLog"
    }
    Write-Host "[Verify] Full smoke log saved to $smokeLog"
}

if ($godotErrors.Count -gt 0) {
    $godotErrors | Select-Object -First 5 | ForEach-Object { Write-Host "[Verify] GODOT ERROR: $_" }
    Write-Error "[Verify] FAIL: Godot emitted $($godotErrors.Count) runtime ERROR/SCRIPT ERROR line(s). Inspect $smokeLog"
    exit 1
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
