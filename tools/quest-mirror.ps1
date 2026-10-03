# Mirror every Quest on this PC over the local Wi-Fi LAN (no internet needed), one window per headset
# titled "Quest 1 - Quest 3S (192.168.0.37)". One action: double-click quest-mirror.cmd.
#
#   1. USB-connected Quests are switched to Wi-Fi adb first (needed after a Quest reboot; the cable can then be
#      removed). Known Quests that dropped off adb are reconnected.
#   2. scrcpy runs view-only (no audio, no input), cropped to the left eye unless -BothEyes.
#
#   powershell -ExecutionPolicy Bypass -File tools/quest-mirror.ps1 [-BothEyes] [-Serial 192.168.0.37:5555]
param([switch]$BothEyes, [string]$Serial = '', [int]$Width = 720)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'quest-adb-common.ps1')

$adb = Get-QuestAdb
$scrcpy = Join-Path $PSScriptRoot 'quest-mirror\scrcpy\scrcpy.exe'
if (-not (Test-Path $scrcpy)) {
    $cmd = Get-Command scrcpy -ErrorAction SilentlyContinue
    if (-not $cmd) {
        throw ("scrcpy not found. Put the official Windows build in tools\quest-mirror\scrcpy\ " +
               "(https://github.com/Genymobile/scrcpy/releases) or run: winget install --exact --id Genymobile.scrcpy")
    }
    $scrcpy = $cmd.Source
}

Enable-QuestWifi $adb | Out-Null
$quests = @(Connect-KnownQuests $adb)
if ($Serial) {
    if ($Serial -notin $quests) { & $adb connect $Serial | Out-Null; $quests = @(Connect-KnownQuests $adb) }
    $quests = @($quests | Where-Object { $_ -eq $Serial })
}
if (-not $quests.Count) {
    throw ('No Quest reachable over Wi-Fi. After a Quest reboot, connect the USB cable once ' +
           '(allow USB debugging in the headset) and run this again.')
}

# scrcpy ships its own adb; a different adb version would restart the running adb server.
$env:ADB = $adb
$index = 0
foreach ($quest in ($quests | Sort-Object { [version](($_ -split ':')[0]) })) {
    $index++
    $model = Get-QuestModel $adb $quest
    $title = "Quest $index - $model ($(($quest -split ':')[0]))"
    $scrcpyArgs = @("--serial=$quest", '--no-audio', '--no-control', '--max-fps=30', '--video-bit-rate=8M',
                    "--window-title=$title", "--window-x=$(40 + ($index - 1) * ($Width + 20))", '--window-y=60',
                    "--window-width=$Width")
    $size = (& $adb -s $quest shell wm size) -join ' '
    if (-not $BothEyes -and $size -match '(\d+)x(\d+)') {
        $w = [int]$Matches[1]; $h = [int]$Matches[2]
        # The Quest mirror frame holds both eyes side by side; show the left half only.
        if ($w -gt $h * 1.2) { $scrcpyArgs += "--crop=$([int]($w / 2)):${h}:0:0" }
    }
    Start-Process -FilePath $scrcpy -ArgumentList $scrcpyArgs | Out-Null
    Write-Host "$title (view only, no audio). Close its window to stop."
}
