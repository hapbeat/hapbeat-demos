<#
.SYNOPSIS
  Imports a .unitypackage into the project using Unity's -importPackage CLI switch.

.DESCRIPTION
  AssetDatabase.ImportPackage() from an -executeMethod entry point does nothing in
  batch mode (the import is queued on the editor loop, which -quit tears down first).
  Unity's own -importPackage switch performs the import synchronously, so package
  import lives here rather than in BatchOps.cs.

.EXAMPLE
  .\tools\import-package.ps1 -PackagePath 'C:\path\to\arena-art.unitypackage'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $PackagePath,

    [string] $ProjectPath = (Split-Path -Parent $PSScriptRoot),

    [string] $UnityExe,

    [int] $TailLines = 30
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PackagePath)) { throw "Package not found: $PackagePath" }
$requiredUnityVersion = '6000.0.59f2'
if ([string]::IsNullOrWhiteSpace($UnityExe)) {
    $UnityExe = Join-Path ${env:ProgramFiles} "Unity\Hub\Editor\$requiredUnityVersion\Editor\Unity.exe"
    if (-not (Test-Path -LiteralPath $UnityExe)) {
        throw "Unity $requiredUnityVersion was not found in the standard Unity Hub location. Install that version or pass -UnityExe '<path-to-Unity.exe>'."
    }
} elseif (-not (Test-Path -LiteralPath $UnityExe)) {
    throw "Unity executable not found: $UnityExe (required version: $requiredUnityVersion)"
}

$logDir = Join-Path $PSScriptRoot 'logs'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
$logPath = Join-Path $logDir ("importPackage-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')

$unityArgs = @('-batchmode', '-quit', '-nographics', '-projectPath', $ProjectPath,
               '-importPackage', $PackagePath, '-logFile', $logPath)

Write-Output "import-package: $PackagePath"
Write-Output "  project: $ProjectPath"
Write-Output "  log    : $logPath"

$proc = Start-Process -FilePath $UnityExe -ArgumentList $unityArgs -PassThru -Wait
$exit = $proc.ExitCode

Write-Output ''
Write-Output "--- log tail ($TailLines lines) ---"
if (Test-Path $logPath) { Get-Content $logPath -Tail $TailLines } else { Write-Output '(no log produced)' }
Write-Output "--- end log tail ---"
Write-Output "exit code: $exit"

exit $exit
