# Mirror the Quest screen on this PC over the local Wi-Fi LAN (no internet needed).
# One action: double-click quest-mirror.cmd. Uses adb + scrcpy; the PC and the Quest must be on the same LAN.
#
#   1. Uses an already connected Wi-Fi adb device, else reconnects to the last known Quest IP.
#   2. After a Quest reboot Wi-Fi adb is off (a non-rooted Quest cannot persist it): if a USB cable
#      is connected, it re-enables Wi-Fi adb and the cable can then be removed.
#   3. Starts scrcpy view-only (no audio, no input), cropped to the left eye unless -BothEyes.
#
#   powershell -ExecutionPolicy Bypass -File tools/quest-mirror.ps1 [-BothEyes] [-Ip 192.168.0.37]
param([switch]$BothEyes, [string]$Ip = '')
$ErrorActionPreference = 'Stop'

$cacheDir = Join-Path $env:LOCALAPPDATA 'Hapbeat'
$cacheFile = Join-Path $cacheDir 'quest-wifi-ip.txt'

function Find-Tool([string]$name) {
    $local = Join-Path $PSScriptRoot "quest-mirror\scrcpy\$name.exe"
    if ($name -eq 'scrcpy' -and (Test-Path $local)) { return $local }
    $cmd = Get-Command $name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

function Get-Devices {
    & $script:adb devices | Select-String '^(\S+)\s+device$' | ForEach-Object { $_.Matches[0].Groups[1].Value }
}

function Connect-Wifi([string]$address) {
    if (-not $address) { return $null }
    $target = if ($address -match ':\d+$') { $address } else { "${address}:5555" }
    $result = (& $script:adb connect $target 2>&1) -join ' '
    if ($result -match 'connected to') { return $target }
    return $null
}

$script:adb = Find-Tool 'adb'
if (-not $script:adb) { throw 'adb not found. Install Android platform-tools and add it to PATH.' }
$scrcpy = Find-Tool 'scrcpy'
if (-not $scrcpy) {
    throw ("scrcpy not found. Put the official Windows build in tools\quest-mirror\scrcpy\ " +
           "(https://github.com/Genymobile/scrcpy/releases) or run: winget install --exact --id Genymobile.scrcpy")
}

$serial = Get-Devices | Where-Object { $_ -match '^\d+\.\d+\.\d+\.\d+:\d+$' } | Select-Object -First 1
if (-not $serial) {
    $known = if ($Ip) { $Ip } elseif (Test-Path $cacheFile) { (Get-Content $cacheFile -Raw).Trim() } else { '' }
    $serial = Connect-Wifi $known
}
if (-not $serial) {
    $usb = Get-Devices | Where-Object { $_ -notmatch ':' } | Select-Object -First 1
    if (-not $usb) {
        throw ('Quest not reachable over Wi-Fi. After a Quest reboot, connect the USB cable once ' +
               '(allow USB debugging in the headset) and run this again.')
    }
    $wlan = (& $script:adb -s $usb shell ip -f inet addr show wlan0 | Select-String 'inet (\d+\.\d+\.\d+\.\d+)')
    if (-not $wlan) { throw 'Quest has no Wi-Fi address (wlan0). Join the same network as this PC.' }
    $address = $wlan.Matches[0].Groups[1].Value
    & $script:adb -s $usb tcpip 5555 | Out-Null
    Start-Sleep -Seconds 3
    $serial = Connect-Wifi $address
    if (-not $serial) { throw "adb connect ${address}:5555 failed." }
    Write-Host 'Wi-Fi adb enabled. The USB cable can now be removed.'
}
New-Item -ItemType Directory -Force $cacheDir | Out-Null
Set-Content -Path $cacheFile -Value ($serial -replace ':\d+$', '') -NoNewline

$scrcpyArgs = @("--serial=$serial", '--no-audio', '--no-control', '--max-fps=30', '--video-bit-rate=8M',
          '--window-title=Quest mirror')
$size = (& $script:adb -s $serial shell wm size) -join ' '
if (-not $BothEyes -and $size -match '(\d+)x(\d+)') {
    $width = [int]$Matches[1]; $height = [int]$Matches[2]
    # The Quest mirror frame holds both eyes side by side; show the left half only.
    if ($width -gt $height * 1.2) { $scrcpyArgs += "--crop=$([int]($width / 2)):${height}:0:0" }
}
Write-Host "Mirroring $serial (view only, no audio). Close the window to stop."
& $scrcpy @scrcpyArgs
