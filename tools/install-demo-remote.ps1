# Install the Android Demo Remote (android/demo-remote) on every Android phone / tablet reachable by adb,
# over USB or over Wi-Fi (Android 11+ "Wireless debugging"). Double-click install-demo-remote.cmd.
#
#   Wi-Fi: once per phone, turn on Developer options > Wireless debugging, tap "Pair device with pairing code"
#   and run:  powershell -File tools/install-demo-remote.ps1 -Pair 192.168.0.50:37123 -Code 123456
#   After that, adb finds the phone by mDNS whenever Wireless debugging is on (same LAN as this PC).
#
#   powershell -ExecutionPolicy Bypass -File tools/install-demo-remote.ps1 [-Build] [-Apk <path>] [-NoLaunch]
param([switch]$Build, [string]$Apk = '', [string]$Pair = '', [string]$Code = '', [switch]$NoLaunch)
$ErrorActionPreference = 'Continue'

# Self-contained (no shared helper): use the adb of the server that is already running, since a client of
# another version restarts the server and drops every Wi-Fi device.
function Get-RemoteAdb {
    $running = Get-CimInstance Win32_Process -Filter "Name='adb.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ExecutablePath -and (Test-Path $_.ExecutablePath) } | Select-Object -First 1
    if ($running) { return $running.ExecutablePath }
    $cmd = Get-Command adb -ErrorAction SilentlyContinue
    if (-not $cmd) { throw 'adb not found. Install Android platform-tools and add it to PATH.' }
    cmd /c "`"$($cmd.Source)`" start-server >nul 2>&1"
    return $cmd.Source
}
function Get-OnlineDevices([string]$adb) {
    & $adb devices | Select-String '^(\S+)\s+device$' | ForEach-Object { $_.Matches[0].Groups[1].Value } |
        Where-Object { $_ -notmatch '^emulator-' }
}
function Get-DeviceModel([string]$adb, [string]$serial) {
    ((& $adb -s $serial shell getprop ro.product.model) -join '').Trim()
}

$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'android\demo-remote'
if (-not $Apk) { $Apk = Join-Path $project 'app\build\outputs\apk\debug\app-debug.apk' }

if ($Build -or -not (Test-Path $Apk)) {
    $jbr = 'C:\Program Files\Android\Android Studio\jbr'
    if (Test-Path $jbr) { $env:JAVA_HOME = $jbr }
    Push-Location $project
    & .\gradlew.bat :app:assembleDebug --console=plain
    $code = $LASTEXITCODE
    Pop-Location
    if ($code -ne 0) { throw "Build failed (exit $code)." }
}
if (-not (Test-Path $Apk)) { throw "APK not found: $Apk" }

$adb = Get-RemoteAdb
if ($Pair) {
    if (-not $Code) { throw '-Pair needs -Code (the 6-digit code shown on the phone).' }
    & $adb pair $Pair $Code
}

# Phones on Wireless debugging advertise _adb-tls-connect; adb usually connects them on its own.
foreach ($line in @(& $adb mdns services 2>$null)) {
    if ($line -match '_adb-tls-connect\._tcp\.?\s+(\d+\.\d+\.\d+\.\d+:\d+)') { & $adb connect $Matches[1] 2>&1 | Out-Null }
}

# One transport per physical phone (USB and Wi-Fi show the same serial number); Quests are skipped.
$phones = [ordered]@{}
foreach ($serial in @(Get-OnlineDevices $adb)) {
    $model = Get-DeviceModel $adb $serial
    if ($model -match '^Quest') { continue }
    $id = ((& $adb -s $serial shell getprop ro.serialno) -join '').Trim()
    if (-not $id) { $id = $serial }
    if (-not $phones.Contains($id)) { $phones[$id] = @{ Serial = $serial; Model = $model } }
}
if (-not $phones.Count) {
    throw ('No Android phone found. USB: connect it and allow USB debugging. Wi-Fi: turn on Developer options > ' +
           'Wireless debugging on the same LAN (first time: run with -Pair <ip:port> -Code <code>).')
}

$failed = 0
foreach ($phone in $phones.Values) {
    $result = (& $adb -s $phone.Serial install -r $Apk 2>&1) -join ' '
    if ($result -notmatch 'Success') { Write-Warning "$($phone.Model) ($($phone.Serial)): install failed: $result"; $failed++; continue }
    Write-Host "$($phone.Model) ($($phone.Serial)): installed."
    if (-not $NoLaunch) { & $adb -s $phone.Serial shell am start -n com.hapbeat.demoremote/.MainActivity | Out-Null }
}
if ($failed) { exit 1 }
