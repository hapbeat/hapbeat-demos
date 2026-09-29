# Re-enable Wi-Fi adb on a USB-connected Quest and connect to it over Wi-Fi.
# Needed after every Quest reboot: a non-rooted Quest cannot persist the adb TCP port
# (setprop persist.adb.tcp.port is refused). Sleep/wake keeps it; a reboot resets it.
#   powershell -File tools/enable-quest-wifi-adb.ps1
$ErrorActionPreference = 'Stop'
$usb = (adb devices | Select-String '^(\S+)\s+device$' | Where-Object { $_.Matches[0].Groups[1].Value -notmatch ':' } |
        Select-Object -First 1)
if (-not $usb) { throw 'No USB-connected Quest. Connect the cable and allow USB debugging in the headset.' }
$serial = $usb.Matches[0].Groups[1].Value
$ip = (adb -s $serial shell ip -f inet addr show wlan0 | Select-String 'inet (\d+\.\d+\.\d+\.\d+)').Matches[0].Groups[1].Value
if (-not $ip) { throw 'Quest has no Wi-Fi address (wlan0). Join the same network as this PC.' }
adb -s $serial tcpip 5555 | Out-Null
Start-Sleep -Seconds 3
adb connect "${ip}:5555"
Write-Host "Quest Wi-Fi adb: ${ip}:5555 (the USB cable can now be removed)"
