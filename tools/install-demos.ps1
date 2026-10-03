# Install the demo APKs on one Quest and write its Hapbeat address (player / group) for every demo.
# Every APK that goes on the Quest is first gathered in quest-apks/ (git-ignored, one file per demo, named after the
# app) and installed from there; -NoCollect installs what is already in quest-apks/ without looking at the builds.
# The address goes to each app's hapbeat-device.json (hapbeat-contracts specs/demo-session.md), so a second
# headset can be set to group 2 at install time without opening each demo.
#
#   powershell -File tools/install-demos.ps1 -Group 1                 # install all, this headset = group 1
#   powershell -File tools/install-demos.ps1 -Group 2 -Serial 192.168.0.38:5555
#   powershell -File tools/install-demos.ps1 -Group 2 -ConfigOnly     # only rewrite the address, no install
#   powershell -File tools/install-demos.ps1 -Only volley,trex        # subset; address left as it is
#   powershell -File tools/install-demos.ps1 -ClearAddress -ConfigOnly
#   powershell -File tools/install-demos.ps1 -CollectOnly              # refresh quest-apks/ only
#
# -Player / -Group: 1..99, or leave out (-1) to not set that axis. Without either, the address files are not
# touched. Values forced in a demo's build settings still win (see the spec).
param(
    [ValidateRange(-1, 99)][int]$Group = -1,
    [ValidateRange(-1, 99)][int]$Player = -1,
    [string]$Serial = '',
    [string[]]$Only = @(),
    [switch]$ConfigOnly,
    [switch]$ClearAddress,
    [switch]$CollectOnly,
    [switch]$NoCollect
)
$ErrorActionPreference = 'Continue'
if ($Group -eq 0 -or $Player -eq 0) { throw 'Player / Group must be 1..99 (or omitted).' }

$root = Split-Path $PSScriptRoot -Parent
# Source = the build output; File = its name in quest-apks/. Default = installed when -Only is not given.
$demos = [ordered]@{
    hub        = @{ Package = 'jp.hapbeat.demohub';            File = 'DemoHub.apk';       Default = $true;  Source = 'unity/demo-hub/Builds/hapbeat-demo-hub.apk' }
    volley     = @{ Package = 'jp.hapbeat.volley';             File = 'Volley.apk';        Default = $true;  Source = 'unity/gloveball/Builds/Android/hapbeat-volley.apk' }
    gloveball  = @{ Package = 'jp.hapbeat.gloveballdemo.v2';   File = 'GloveBall.apk';     Default = $true;  Source = 'unity/gloveball/tools/artifacts/gloveball_v2.apk' }
    boxing     = @{ Package = 'com.hapbeat.boxing';            File = 'Boxing.apk';        Default = $true;  Source = 'unity/boxing-vr/Builds/HapbeatBoxing.apk' }
    handdemo   = @{ Package = 'com.Hapbeat.HapticHandDemo_G2'; File = 'HandDemo.apk';      Default = $true;  Source = 'unity/handdemo/Build/HandDemo-switch-fixed.apk' }
    safetymill = @{ Package = 'com.hapbeat.safetymill';        File = 'SafetyMillVR.apk';  Default = $true;  Source = 'unreal/safety-mill-vr/Builds/QuestHands/Android_ASTC/SafetyMillVR-arm64.apk' }
    trex       = @{ Package = 'com.hapbeat.trexencounter';     File = 'TRexEncounter.apk'; Default = $true;  Source = 'unreal/trex-encounter/Builds/Quest/Android_ASTC/HapbeatTrexDemo-arm64.apk' }
    energyduel = @{ Package = 'jp.hapbeat.energyduel';         File = 'EnergyDuel.apk';    Default = $true;  Source = 'unity/energy-duel/Builds/EnergyDuel.apk' }
    fps        = @{ Package = 'com.hapbeat.fpsdemo';           File = 'FPS.apk';           Default = $true;  Source = 'unity/fps/Builds/Quest/HapbeatFPS-Quest.apk' }
}
$apkDir = Join-Path $root 'quest-apks'
$keys = if ($Only.Count) { $Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ } }
        else { $demos.Keys | Where-Object { $demos[$_].Default } }
foreach ($key in $keys) { if (-not $demos.Contains($key)) { throw "Unknown demo '$key'. Known: $($demos.Keys -join ', ')" } }

# Gather the newest builds into quest-apks/ (a build is copied only when it is newer than the collected file).
if (-not $NoCollect -and -not $ConfigOnly) {
    New-Item -ItemType Directory -Force $apkDir | Out-Null
    foreach ($key in $keys) {
        $source = Join-Path $root $demos[$key].Source
        $target = Join-Path $apkDir $demos[$key].File
        if (-not (Test-Path $source)) { continue }
        if (-not (Test-Path $target) -or (Get-Item $source).LastWriteTime -gt (Get-Item $target).LastWriteTime) {
            Copy-Item $source $target -Force
            Write-Host "${key}: collected $($demos[$key].File) ($([math]::Round((Get-Item $target).Length / 1MB)) MB, built $((Get-Item $source).LastWriteTime.ToString('MM-dd HH:mm')))"
        }
    }
}
if ($CollectOnly) { Write-Host "APKs in $apkDir"; exit 0 }

. (Join-Path $PSScriptRoot 'quest-adb-common.ps1')
$adb = Get-QuestAdb
if ($Serial) {
    if ($Serial -match '^\d+\.\d+\.\d+\.\d+:\d+$' -and $Serial -notin @(Get-OnlineDevices $adb)) { & $adb connect $Serial | Out-Null }
} else {
    # Another tool (e.g. a Unity build) may have restarted the adb server; reconnect known Wi-Fi Quests first.
    Connect-KnownQuests $adb | Out-Null
    $online = @(Get-OnlineDevices $adb)
    if ($online.Count -ne 1) {
        throw "Connect exactly one Quest or pass -Serial (online: $($online -join ', '))."
    }
    $Serial = $online[0]
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
        $apk = Join-Path $apkDir $demo.File
        if (-not (Test-Path $apk)) {
            Write-Warning "${key}: no quest-apks/$($demo.File) (build $($demo.Source) first); skipped"
            if ($Only.Count) { $failed += $key }   # only an explicitly requested demo counts as a failure
            continue
        }
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
