# Re-enable Wi-Fi adb on every USB-connected Quest and connect to it over Wi-Fi (double-click
# enable-quest-wifi-adb.cmd). Needed after every Quest reboot: a non-rooted Quest cannot persist the adb TCP
# port (setprop persist.adb.tcp.port is refused). Sleep/wake keeps it; a reboot resets it.
# The Quest needs Developer Mode and must be on the same LAN as this PC.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'quest-adb-common.ps1')
$adb = Get-QuestAdb
$new = @(Enable-QuestWifi $adb)
if (-not $new.Count) {
    $known = @(Connect-KnownQuests $adb)
    if ($known.Count) { Write-Host "No USB Quest. Already on Wi-Fi: $($known -join ', ')"; exit 0 }
    throw 'No USB-connected Quest. Connect the cable and allow USB debugging in the headset.'
}
