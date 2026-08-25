<#
.SYNOPSIS
  Runs a Unity batch-mode -executeMethod call (or the test runner) and reports exit code + log tail.

.EXAMPLE
  .\tools\run-unity.ps1 -Method GloveBallDemo.Editor.BatchOps.ConfigureProject
  .\tools\run-unity.ps1 -Method GloveBallDemo.Editor.BatchOps.CaptureArenaScreenshot -Graphics
  .\tools\run-unity.ps1 -Method Foo.Bar -ExtraArgs @('-gbArg','value')
  .\tools\run-unity.ps1 -RunTests EditMode
  .\tools\run-unity.ps1 -Method GloveBallDemo.Editor.BatchOps.SmokePlay -Graphics -NoQuit

.NOTES
  Unity.exe is a GUI-subsystem binary, so PowerShell's call operator does NOT wait for it.
  Start-Process -Wait is required to get a real exit code.

  -NoQuit is required for anything that must keep ticking the editor loop after
  -executeMethod returns (entering play mode, for instance). Such a method is
  responsible for calling EditorApplication.Exit() itself.
#>
[CmdletBinding(DefaultParameterSetName = 'Method')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Method')]
    [string] $Method,

    [Parameter(Mandatory = $true, ParameterSetName = 'Tests')]
    [ValidateSet('EditMode', 'PlayMode')]
    [string] $RunTests,

    [string] $ProjectPath = (Split-Path -Parent $PSScriptRoot),

    [string] $UnityExe,

    # Screenshot capture and play mode need a real GPU context, so -nographics must be omitted there.
    [switch] $Graphics,

    # Omit -quit for methods that drive the editor loop themselves.
    [switch] $NoQuit,

    [string[]] $ExtraArgs = @(),

    [int] $TailLines = 60
)

$ErrorActionPreference = 'Stop'

$requiredUnityVersion = '6000.0.59f2'
if ([string]::IsNullOrWhiteSpace($UnityExe)) {
    $UnityExe = Join-Path ${env:ProgramFiles} "Unity\Hub\Editor\$requiredUnityVersion\Editor\Unity.exe"
    if (-not (Test-Path -LiteralPath $UnityExe)) {
        throw "Unity $requiredUnityVersion was not found in the standard Unity Hub location. Install that version or pass -UnityExe '<path-to-Unity.exe>'."
    }
} elseif (-not (Test-Path -LiteralPath $UnityExe)) {
    throw "Unity executable not found: $UnityExe (required version: $requiredUnityVersion)"
}
if (-not (Test-Path -LiteralPath $ProjectPath)) { throw "Project path not found: $ProjectPath" }

$logDir = Join-Path $PSScriptRoot 'logs'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'

if ($PSCmdlet.ParameterSetName -eq 'Tests') {
    $label = "tests-$RunTests"
} else {
    $label = ($Method -split '\.')[-1]
}

$logPath = Join-Path $logDir "$label-$stamp.log"

$unityArgs = @('-batchmode', '-projectPath', $ProjectPath, '-logFile', $logPath)

if ($PSCmdlet.ParameterSetName -eq 'Tests') {
    $resultsPath = Join-Path $logDir "$label-$stamp.xml"
    $unityArgs += @('-runTests', '-testPlatform', $RunTests, '-testResults', $resultsPath)
} else {
    if (-not $NoQuit) { $unityArgs += '-quit' }
    $unityArgs += @('-executeMethod', $Method)
}

if (-not $Graphics) { $unityArgs += '-nographics' }
$unityArgs += $ExtraArgs

Write-Output "run-unity: $label"
Write-Output "  project : $ProjectPath"
Write-Output "  log     : $logPath"
Write-Output "  args    : $($unityArgs -join ' ')"

# -Wait also waits on Unity's child processes (licensing client, package manager), which can
# outlive a failed run and hang the call. Wait on the Unity process itself instead.
$proc = Start-Process -FilePath $UnityExe -ArgumentList $unityArgs -PassThru
$proc.WaitForExit()
$exit = $proc.ExitCode

Write-Output ''
Write-Output "--- log tail ($TailLines lines) ---"
if (Test-Path $logPath) { Get-Content $logPath -Tail $TailLines } else { Write-Output '(no log produced)' }
Write-Output "--- end log tail ---"

if ($PSCmdlet.ParameterSetName -eq 'Tests') {
    if (Test-Path $resultsPath) {
        [xml] $xml = Get-Content $resultsPath
        $run = $xml.'test-run'
        Write-Output "test results: total=$($run.total) passed=$($run.passed) failed=$($run.failed) skipped=$($run.skipped) inconclusive=$($run.inconclusive)"
        Write-Output "results xml : $resultsPath"
        if ([int] $run.failed -gt 0) {
            Write-Output '--- failures ---'
            $xml.SelectNodes('//test-case[@result="Failed"]') | ForEach-Object {
                Write-Output "FAIL $($_.fullname)"
                Write-Output "     $($_.failure.message.'#cdata-section')"
            }
        }
    } else {
        Write-Output 'test results: (no xml produced)'
    }
}

Write-Output "exit code: $exit"
Write-Output "log file : $logPath"

exit $exit
