param(
    [ValidateSet('Content','ContentPreview','Polish','Upgrade','Configure','Validate','Tests','InputTests','Smoke','Capture','Windows','Android','Simulator','AirLink','SimulatorSmoke')]
    [string]$Task = 'Validate',
    [string]$UnityExe = 'M:/GameEngine/Unity/Editor/6000.3.12f1/Editor/Unity.exe'
)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$logs = Join-Path $project 'Logs'
New-Item -ItemType Directory -Force -Path $logs | Out-Null
$arguments = @('-batchmode', '-projectPath', ('"' + $project + '"'), '-logFile', ('"' + (Join-Path $logs ($Task + '.log')) + '"'))
if ($Task -eq 'Android') { $arguments += @('-buildTarget', 'Android') }
else { $arguments += @('-buildTarget', 'Win64') }
if ($Task -in @('Validate','Tests','Configure')) { $arguments += '-nographics' }
if ($Task -eq 'Tests') { $arguments += @('-runTests', '-testPlatform', 'EditMode', '-testResults', ('"' + (Join-Path $logs 'tests.xml') + '"')) }
elseif ($Task -eq 'InputTests') { $arguments += @('-runTests', '-testPlatform', 'PlayMode', '-assemblyNames', 'Hapbeat.Boxing.InputTests', '-testResults', ('"' + (Join-Path $logs 'input-tests.xml') + '"')) }
else {
    $method = switch ($Task) {
        'Content' { 'Hapbeat.Boxing.Editor.BoxingContent.Upgrade' }
        'ContentPreview' { 'Hapbeat.Boxing.Editor.BoxingContent.Preview' }
        'Polish' { 'Hapbeat.Boxing.Editor.BoxingProject.Polish' }
        'Upgrade' { 'Hapbeat.Boxing.Editor.BoxingProject.Upgrade' }
        'Configure' { 'Hapbeat.Boxing.Editor.BoxingProject.Configure' }
        'Validate' { 'Hapbeat.Boxing.Editor.BoxingProject.Validate' }
        'Smoke' { 'Hapbeat.Boxing.Editor.BoxingVerification.Smoke' }
        'Capture' { 'Hapbeat.Boxing.Editor.BoxingVerification.Capture' }
        'Windows' { 'Hapbeat.Boxing.Editor.BoxingProject.BuildWindows' }
        'Android' { 'Hapbeat.Boxing.Editor.BoxingProject.BuildAndroid' }
        'Simulator' { 'Hapbeat.Boxing.Editor.BoxingSimulator.Enable' }
        'AirLink' { 'Hapbeat.Boxing.Editor.BoxingSimulator.Disable' }
        'SimulatorSmoke' { 'Hapbeat.Boxing.Editor.BoxingSimulatorVerification.Run' }
    }
    $arguments += @('-executeMethod', $method)
    if ($Task -notin @('Smoke','SimulatorSmoke')) { $arguments += '-quit' }
}
$process = Start-Process -FilePath $UnityExe -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output "Unity $Task PID=$($process.Id)"
$process.WaitForExit()
Write-Output "Unity $Task exit=$($process.ExitCode) log=$logs/$Task.log"
if ($process.ExitCode -ne 0) { exit $process.ExitCode }
& (Join-Path $PSScriptRoot 'assert-unity-log.ps1') -LogPath (Join-Path $logs ($Task + '.log'))
if ($Task -in @('Tests', 'InputTests')) {
    $resultName = if ($Task -eq 'InputTests') { 'input-tests.xml' } else { 'tests.xml' }
    [xml]$results = Get-Content -LiteralPath (Join-Path $logs $resultName) -Raw
    if ($results.'test-run'.result -ne 'Passed' -or [int]$results.'test-run'.total -eq 0) { throw 'Unity tests did not pass.' }
}
exit $process.ExitCode
