param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Unity.exe',
    [switch]$Rebuild
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (-not (Test-Path -LiteralPath $UnityPath)) { throw "Unity Editor not found: $UnityPath" }
if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'Assets/MagicaCloth2/Scripts/Core/Cloth/MagicaCloth.cs'))) {
    throw 'Import your licensed Magica Cloth 2 package before validation.'
}
New-Item -ItemType Directory -Force -Path (Join-Path $projectRoot 'Logs') | Out-Null
function Invoke-UnityValidation([string]$Arguments) {
    $job = Start-Process -FilePath $UnityPath -ArgumentList "-batchmode -projectPath `"$projectRoot`" $Arguments" -WindowStyle Hidden -PassThru
    $job.WaitForExit()
    if ($job.ExitCode -ne 0) { throw "Unity failed with exit code $($job.ExitCode). See Logs/GBHN-*.log." }
}
if ($Rebuild) { Invoke-UnityValidation '-executeMethod PirateAlliance.Characters.Editor.GBHNClothSetup.BatchBuild -logFile Logs/GBHN-build.log' }
$resultPath = Join-Path $projectRoot 'Logs/GBHN-tests.xml'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
Invoke-UnityValidation '-runTests -testPlatform PlayMode -testFilter GBHNClothTests -testResults Logs/GBHN-tests.xml -logFile Logs/GBHN-tests.log'
[xml]$results = Get-Content -LiteralPath $resultPath
if ($results.'test-run'.result -ne 'Passed' -or [int]$results.'test-run'.passed -lt 4) { throw 'The GBHN cloth tests did not all pass.' }
Copy-Item -LiteralPath $resultPath -Destination (Join-Path $projectRoot 'Documentation/GBHN/Validation/playmode-results.xml')
Write-Output 'GBHN cloth generation, movement, culling and four-character performance checks passed.'
