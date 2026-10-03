# Install the demo APKs on one Quest and write its Hapbeat address (player / group) for every demo.
# The address goes to each app's hapbeat-device.json (hapbeat-contracts specs/demo-session.md), so a second
# headset can be set to group 2 at install time without opening each demo.
#
#   powershell -File tools/install-demos.ps1 -Group 1                 # install all, this headset = group 1
#   powershell -File tools/install-demos.ps1 -Group 2 -Serial 192.168.0.38:5555
#   powershell -File tools/install-demos.ps1 -Group 2 -ConfigOnly     # only rewrite the address, no install
#   powershell -File tools/install-demos.ps1 -Only volley,trex        # subset; address left as it is
#   powershell -File tools/install-demos.ps1 -ClearAddress -ConfigOnly
#
# -Player / -Group: 1..99, or leave out (-1) to not set that axis. Without either, the address files are not
# touched. Values forced in a demo's build settings still win (see the spec).
param(
    [ValidateRange(-1, 99)][int]$Group = -1,
    [ValidateRange(-1, 99)][int]$Player = -1,
    [string]$Serial = '',
    [string[]]$Only = @(),
    [switch]$ConfigOnly,
    [switch]$ClearAddress
)
$ErrorActionPreference = 'Stop'
if ($Group -eq 0 -or $Player -eq 0) { throw 'Player / Group must be 1..99 (or omitted).' }

$root = Split-Path $PSScriptRoot -Parent
$demos = [ordered]@{
    hub        = @{ Package = 'jp.hapbeat.demohub';               Apk = 'unity/demo-hub/Builds/hapbeat-demo-hub.apk' }
    volley     = @{ Package = 'jp.hapbeat.volley';                Apk = 'unity/gloveball/Builds/Android/hapbeat-volley.apk' }
    gloveball  = @{ Package = 'jp.hapbeat.gloveballdemo.v2';      Apk = 'unity/gloveball/tools/artifacts/gloveball_v2.apk' }
    boxing     = @{ Package = 'com.hapbeat.boxing';               Apk = 'unity/boxing-vr/Builds/HapbeatBoxing.apk' }
    handdemo   = @{ Package = 'com.Hapbeat.HapticHandDemo_G2';    Apk = 'unity/handdemo/Build/HandDemo-switch-fixed.apk' }
    safetymill = @{ Package = 'com.hapbeat.safetymill';           Apk = 'unreal/safety-mill-vr/Builds/QuestHands/Android_ASTC/SafetyMillVR-arm64.apk' }
    trex       = @{ Package = 'com.hapbeat.trexencounter';        Apk = 'unreal/trex-encounter/Builds/Quest/Android_ASTC/HapbeatTrexDemo-arm64.apk' }
}
$keys = if ($Only.Count) { $Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ } } else { $demos.Keys }
foreach ($key in $keys) { if (-not $demos.Contains($key)) { throw "Unknown demo '$key'. Known: $($demos.Keys -join ', ')" } }

$adb = (Get-Command adb -ErrorAction SilentlyContinue).Source
if (-not $adb) { throw 'adb not found on PATH.' }
function Get-Online {
    & $adb devices | Select-String '^(\S+)\s+device$' | ForEach-Object { $_.Matches[0].Groups[1].Value } |
        Where-Object { $_ -notmatch '^emulator-' }
}
$cacheFile = Join-Path $env:LOCALAPPDATA 'Hapbeat\quest-wifi-ip.txt'   # shared with quest-mirror.ps1
if ($Serial -match '^\d+\.\d+\.\d+\.\d+:\d+$' -and $Serial -notin @(Get-Online)) { & $adb connect $Serial | Out-Null }
if (-not $Serial) {
    $online = Get-Online
    # Another tool (e.g. a Unity build) may have restarted the adb server; reconnect the last Wi-Fi Quest once.
    if (-not $online -and (Test-Path $cacheFile)) {
        & $adb connect "$((Get-Content $cacheFile -Raw).Trim()):5555" | Out-Null
        $online = Get-Online
    }
    if (@($online).Count -ne 1) {
        throw "Connect exactly one Quest or pass -Serial (online: $(@($online) -join ', '))."
    }
    $Serial = @($online)[0]
}
Write-Host "Quest: $Serial"

$writeAddress = $ClearAddress -or $Group -ne -1 -or $Player -ne -1
$tmp = $null
if ($writeAddress -and -not $ClearAddress) {
    $tmp = New-TemporaryFile
    [IO.File]::WriteAllText($tmp, "{`"version`":1,`"player`":$Player,`"group`":$Group}`n", (New-Object Text.UTF8Encoding $false))
}

$failed = @()
foreach ($key in $keys) {
    $demo = $demos[$key]
    if (-not $ConfigOnly) {
        $apk = Join-Path $root $demo.Apk
        if (-not (Test-Path $apk)) { Write-Warning "${key}: APK not built ($($demo.Apk))"; $failed += $key; continue }
        $result = (& $adb -s $Serial install -r $apk 2>&1) -join ' '
        if ($result -notmatch 'Success') { Write-Warning "${key}: install failed: $result"; $failed += $key; continue }
        Write-Host "${key}: installed"
    }
    if (-not $writeAddress) { continue }
    $dir = "/sdcard/Android/data/$($demo.Package)/files"
    if ($ClearAddress) {
        & $adb -s $Serial shell "rm -f $dir/hapbeat-device.json" | Out-Null
        Write-Host "${key}: address cleared"
        continue
    }
    & $adb -s $Serial shell "mkdir -p $dir" | Out-Null
    & $adb -s $Serial push $tmp "$dir/hapbeat-device.json" 2>&1 | Out-Null
    $check = (& $adb -s $Serial shell "cat $dir/hapbeat-device.json" 2>&1) -join ''
    if ($check -match "`"group`":$Group\b" -and $check -match "`"player`":$Player\b") { Write-Host "${key}: address player=$Player group=$Group" }
    else { Write-Warning "${key}: address not written ($check)"; $failed += $key }
}
if ($tmp) { Remove-Item $tmp -ErrorAction SilentlyContinue }
if ($writeAddress -and -not $ClearAddress) { Write-Host 'The address is read when each demo starts; restart running demos.' }
if ($failed.Count) { throw "Failed: $($failed -join ', ')" }
