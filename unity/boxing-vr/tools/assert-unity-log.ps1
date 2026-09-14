param([Parameter(Mandatory)][string]$LogPath)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $LogPath)) { throw "Unity log missing: $LogPath" }
# Unity may return exit 0 and run executeMethod despite earlier native asset parse failures.
$failures = @(Select-String -LiteralPath $LogPath -Pattern 'Unable to parse file|Parser Failure|error CS\d+|Scripts have compiler errors|Aborting batchmode due to|^\s*(?:[\w.]+Exception|Assertion failed)[\s:]')
if ($failures.Count -gt 0) {
    $failures | Select-Object -First 12 | ForEach-Object { Write-Output $_.Line }
    throw "UNITY_LOG_CHECK: FAIL ($($failures.Count) errors): $LogPath"
}
Write-Output "UNITY_LOG_CHECK: PASS: $LogPath"
