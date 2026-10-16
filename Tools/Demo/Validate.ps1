param(
    [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Unity.exe',
    [switch]$Build
)
$ErrorActionPreference = 'Stop'
$demoProject = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (!(Test-Path -LiteralPath $UnityEditor)) { throw "Unity editor not found: $UnityEditor" }
$demoLfsFiles = git -C $demoProject lfs ls-files
if ($LASTEXITCODE -ne 0) { throw 'Git LFS must be installed before validation.' }
if ($demoLfsFiles | Where-Object { $_ -match '^[0-9a-f]+ - Assets/' }) { throw 'Run git lfs pull before validating the demo.' }
# Unity 6000.3 batch startup needs a default Search index even without an interactive editor session.
$demoSearchIndex = Join-Path $demoProject 'UserSettings/Search.index'
if (!(Test-Path -LiteralPath $demoSearchIndex)) {
    New-Item -ItemType Directory -Force -Path (Split-Path $demoSearchIndex) | Out-Null
    '{"name":"Assets","roots":["Assets"],"includes":[],"excludes":[],"options":{"types":true,"properties":true,"extended":false,"dependencies":true},"baseScore":999}' | Set-Content -LiteralPath $demoSearchIndex -Encoding utf8
}
$demoChecks = @(
    @('flow', 'AcRoguelike.Liminal.Editor.LoadoutValidation.RunBatch'),
    @('gate', 'AcRoguelike.Liminal.EditorTests.GateMissionValidation.Run'),
    @('game-boss', 'AcRoguelike.GameTheme.Editor.GameTetrominoBossValidation.RunBatch'),
    @('traffic-boss', 'AcRoguelike.Liminal.EditorTests.TrafficLightBossPlayValidation.Run')
)
if ($Build) { $demoChecks += ,@('build', 'AcRoguelike.Liminal.Editor.DemoBuild.BuildWindows') }
New-Item -ItemType Directory -Force -Path (Join-Path $demoProject 'Logs') | Out-Null
foreach ($demoCheck in $demoChecks) {
    $demoLog = Join-Path $demoProject ('Logs/demo-' + $demoCheck[0] + '.log')
    Write-Host ('Running demo check: ' + $demoCheck[0])
    $demoArguments = @('-batchmode', '-projectPath', ('"' + $demoProject + '"'), '-executeMethod', $demoCheck[1], '-logFile', ('"' + $demoLog + '"'))
    $demoProcess = Start-Process -FilePath $UnityEditor -ArgumentList $demoArguments -PassThru -Wait -WindowStyle Hidden
    if ($demoProcess.ExitCode -ne 0) { throw "Demo check failed ($($demoCheck[0])). See $demoLog" }
}
if ($Build) {
    $demoExe = Join-Path $demoProject 'Builds/PirateAllianceDemo/PirateAllianceDemo.exe'
    $demoSmokeLog = Join-Path $demoProject 'Logs/demo-player.log'
    $demoPlayer = Start-Process -FilePath $demoExe -ArgumentList @('-batchmode', '-demoSmokeTest', '-screen-width', '1280', '-screen-height', '720', '-logFile', ('"' + $demoSmokeLog + '"')) -PassThru -Wait -WindowStyle Hidden
    if ($demoPlayer.ExitCode -ne 0) { throw "Packaged demo smoke check failed. See $demoSmokeLog" }
}
Write-Host 'All requested demo checks passed.'
