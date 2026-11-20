$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot
$csproj = Join-Path $projectRoot 'ACT Spike.csproj'
$godotConsole = if ($env:GODOT_CONSOLE) { $env:GODOT_CONSOLE } else { 'D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe' }
$smokeLog = Join-Path $projectRoot '.godot\headless-smoke.log'

Write-Host '[Verify] Building C# project (warnings as errors)...'
& dotnet build $csproj --no-restore -warnaserror
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
try {
    $env:ACT_SMOKE = '1'
    & $godotConsole --headless --path $projectRoot --log-file $smokeLog --quit-after 3600
    $godotExit = $LASTEXITCODE
}
finally {
    if ($null -eq $prevSmoke) { Remove-Item Env:ACT_SMOKE -ErrorAction SilentlyContinue }
    else { $env:ACT_SMOKE = $prevSmoke }
}

if ($godotExit -ne 0) {
    Write-Error "[Verify] FAIL: Godot headless smoke exited with code $godotExit"
    exit $godotExit
}

if (-not (Test-Path $smokeLog)) {
    Write-Error "[Verify] FAIL: Smoke log missing at $smokeLog"
    exit 1
}

$logContent = Get-Content $smokeLog -Raw
$hasAllPass = $logContent -match '\[Smoke\] ALL PASS'
$hasResetPass = $logContent -match '\[Smoke\] PASS R reset'
$hasFail = $logContent -match '\[Smoke\] FAIL'

if (-not $hasAllPass -or -not $hasResetPass -or $hasFail) {
    Write-Error "[Verify] FAIL: Smoke contract incomplete (ALL PASS=$hasAllPass, R reset=$hasResetPass, FAIL=$hasFail). Inspect $smokeLog"
    exit 1
}

Write-Host '[Verify] PASS: clean 0-warning build + full headless smoke (ALL PASS & R reset).'
exit 0
