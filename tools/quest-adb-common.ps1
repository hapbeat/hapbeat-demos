# Shared helpers for the Quest tools (dot-source this file). Keeps the known Quest Wi-Fi addresses in
# %LOCALAPPDATA%\Hapbeat\quest-wifi-ip.txt, one IPv4 per line, so several headsets reconnect after an adb restart.
$script:QuestCacheFile = Join-Path $env:LOCALAPPDATA 'Hapbeat\quest-wifi-ip.txt'

function Get-QuestAdb {
    $cmd = Get-Command adb -ErrorAction SilentlyContinue
    if (-not $cmd) { throw 'adb not found. Install Android platform-tools and add it to PATH.' }
    # Start the server up front: its "daemon not running; starting now" stderr would otherwise surface as an
    # error from the first real command under Windows PowerShell.
    cmd /c "`"$($cmd.Source)`" start-server >nul 2>&1"
    return $cmd.Source
}

function Get-OnlineDevices([string]$adb) {
    # Only headsets: other Android devices on adb (e.g. the Demo Switch remote phone) are skipped.
    & $adb devices | Select-String '^(\S+)\s+device$' | ForEach-Object { $_.Matches[0].Groups[1].Value } |
        Where-Object { $_ -notmatch '^emulator-' } | Where-Object { (Get-QuestModel $adb $_) -match 'Quest' }
}

function Get-KnownQuestIps {
    if (-not (Test-Path $script:QuestCacheFile)) { return @() }
    @(Get-Content $script:QuestCacheFile | ForEach-Object { ($_ -split '\s+')[0].Trim() } |
        Where-Object { $_ -match '^\d+\.\d+\.\d+\.\d+$' } | Select-Object -Unique)
}

function Add-KnownQuestIp([string]$ip) {
    New-Item -ItemType Directory -Force (Split-Path $script:QuestCacheFile) | Out-Null
    $ips = @(Get-KnownQuestIps) + $ip | Select-Object -Unique
    Set-Content -Path $script:QuestCacheFile -Value $ips
}

# Every USB-connected Quest: switch adb to TCP 5555 and connect over Wi-Fi. Returns the new ip:port serials.
function Enable-QuestWifi([string]$adb) {
    $connected = @()
    foreach ($usb in @(Get-OnlineDevices $adb | Where-Object { $_ -notmatch ':' })) {
        $wlan = & $adb -s $usb shell ip -f inet addr show wlan0 | Select-String 'inet (\d+\.\d+\.\d+\.\d+)'
        if (-not $wlan) { Write-Warning "$usb has no Wi-Fi address (wlan0). Join the same network as this PC."; continue }
        $ip = $wlan.Matches[0].Groups[1].Value
        & $adb -s $usb tcpip 5555 | Out-Null
        Start-Sleep -Seconds 3
        if (((& $adb connect "${ip}:5555" 2>&1) -join ' ') -match 'connected to') {
            Add-KnownQuestIp $ip
            $connected += "${ip}:5555"
            Write-Host "Wi-Fi adb: ${ip}:5555 ($(Get-QuestModel $adb "${ip}:5555")). The USB cable can be removed."
        } else { Write-Warning "adb connect ${ip}:5555 failed." }
    }
    return $connected
}

# Reconnect every known Quest that is not online (e.g. after another tool restarted the adb server).
function Connect-KnownQuests([string]$adb) {
    $online = @(Get-OnlineDevices $adb)
    foreach ($ip in Get-KnownQuestIps) {
        if ("${ip}:5555" -in $online) { continue }
        & $adb connect "${ip}:5555" 2>&1 | Out-Null
    }
    @(Get-OnlineDevices $adb | Where-Object { $_ -match '^\d+\.\d+\.\d+\.\d+:\d+$' })
}

function Get-QuestModel([string]$adb, [string]$serial) {
    ((& $adb -s $serial shell getprop ro.product.model) -join '').Trim()
}
