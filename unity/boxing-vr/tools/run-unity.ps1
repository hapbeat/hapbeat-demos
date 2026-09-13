param(
    [ValidateSet('Polish','Upgrade','Configure','Validate','Tests','Smoke','Capture','Windows','Android')]
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
else {
    $method = switch ($Task) {
        'Polish' { 'Hapbeat.Boxing.Editor.BoxingProject.Polish' }
        'Upgrade' { 'Hapbeat.Boxing.Editor.BoxingProject.Upgrade' }
        'Configure' { 'Hapbeat.Boxing.Editor.BoxingProject.Configure' }
        'Validate' { 'Hapbeat.Boxing.Editor.BoxingProject.Validate' }
        'Smoke' { 'Hapbeat.Boxing.Editor.BoxingVerification.Smoke' }
        'Capture' { 'Hapbeat.Boxing.Editor.BoxingVerification.Capture' }
        'Windows' { 'Hapbeat.Boxing.Editor.BoxingProject.BuildWindows' }
        'Android' { 'Hapbeat.Boxing.Editor.BoxingProject.BuildAndroid' }
    }
    $arguments += @('-executeMethod', $method)
    if ($Task -ne 'Smoke') { $arguments += '-quit' }
}
$process = Start-Process -FilePath $UnityExe -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output "Unity $Task PID=$($process.Id)"
$process.WaitForExit()
Write-Output "Unity $Task exit=$($process.ExitCode) log=$logs/$Task.log"
exit $process.ExitCode
