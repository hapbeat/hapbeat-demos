param(
    [string]$Serial,
    [string]$Adb = 'M:/GameEngine/Unity/Editor/6000.3.12f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe'
)
$ErrorActionPreference = 'Stop'
$bundle = Join-Path $PSScriptRoot '../Builds/DemoSwitch'
$apks = @('GloveBallDemo.apk','HandDemo.apk','HapbeatBoxing.apk') | ForEach-Object { Join-Path $bundle $_ }
foreach ($apk in $apks) { if (-not (Test-Path -LiteralPath $apk)) { throw "Missing APK: $apk" } }
if (-not $Serial) {
    $devices = @(& $Adb devices | Select-String '^([^\s]+)\s+device$' | ForEach-Object { $_.Matches[0].Groups[1].Value })
    if ($LASTEXITCODE -ne 0 -or $devices.Count -ne 1) { throw 'Connect exactly one authorized Quest, or specify -Serial.' }
    $Serial = $devices[0]
}
foreach ($apk in $apks) {
    & $Adb -s $Serial install -r $apk
    if ($LASTEXITCODE -ne 0) { throw 'Install failed. No uninstall or data clearing was attempted; check the adb error above.' }
}
Write-Output 'Installed all three APKs. Start one demo on Quest, then use the MCU A/B/C buttons. No app or haptic playback was launched by this script.'
