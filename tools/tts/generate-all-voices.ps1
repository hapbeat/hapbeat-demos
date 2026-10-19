# Re-voice every demo after editing tools/tts/voice.json (or any voice-lines.json): generates the changed lines
# for each demo, then imports them (Unreal: editor commandlet; Unity: the WAVs are already under Assets/ and are
# imported on the next editor open or build). Requires the AivisSpeech engine on 127.0.0.1:10101 (see README.md).
# Every line is normalized to -20 LUFS (ffmpeg); -NormalizeOnly re-normalizes the existing WAVs without the engine.
# Close the Unreal editors of these projects first. Nothing is played.
#   powershell -File tools/tts/generate-all-voices.ps1 [-Only trex,safetymill,handdemo,energyduel,boxing,fps] [-SkipImport] [-NormalizeOnly]
param([string[]]$Only = @(), [switch]$SkipImport, [switch]$NormalizeOnly, [string]$EngineRoot = $(if ($env:UE_ROOT) { $env:UE_ROOT } else { 'M:\GameEngine\UnrealEngine\UE_5.6' }))
$ErrorActionPreference = 'Continue'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$demos = [ordered]@{
    trex       = @{ Dir = 'unreal/trex-encounter'; Out = 'Voice/wav'; Project = 'HapbeatTrexDemo.uproject'; Import = 'Scripts/import_voice.py' }
    safetymill = @{ Dir = 'unreal/safety-mill-vr'; Out = 'Voice/wav'; Project = 'SafetyMillVR.uproject'; Import = 'Scripts/import_voice.py' }
    handdemo   = @{ Dir = 'unity/handdemo';        Out = 'Assets/HandDemo/Resources/HandDemoVoice' }
    energyduel = @{ Dir = 'unity/energy-duel';     Out = 'Assets/EnergyDuel/Audio/Narration/ja' }
    boxing     = @{ Dir = 'unity/boxing-vr';       Out = 'Assets/Boxing/Audio/Tutorial' }
    fps        = @{ Dir = 'unity/fps';             Out = 'Assets/FPS/Resources/FpsVoice' }
}
$keys = if ($Only.Count) { $Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ } } else { $demos.Keys }
$env:PYTHONIOENCODING = 'utf-8'
$failed = @()
foreach ($key in $keys) {
    $demo = $demos[$key]
    if (-not $demo) { throw "Unknown demo '$key'. Known: $($demos.Keys -join ', ')" }
    $dir = Join-Path $root $demo.Dir
    Write-Host "== ${key}"
    $extra = @()
    if ($NormalizeOnly) { $extra += '--normalize-only' }
    python (Join-Path $PSScriptRoot 'generate-voice.py') (Join-Path $dir 'Voice/voice-lines.json') (Join-Path $dir $demo.Out) @extra
    if ($LASTEXITCODE -ne 0) { $failed += $key; continue }
    if ($SkipImport -or -not $demo.Project) { continue }
    $cmd = Join-Path $EngineRoot 'Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
    $log = & $cmd (Join-Path $dir $demo.Project) -run=PythonScript "-script=$(Join-Path $dir $demo.Import)" -NoSound -nullrhi -unattended 2>&1
    # Headless -NoSound imports return 1 because of a harmless BINKA-decoder ensure; the last *_VOICE_IMPORTED line is the verdict.
    $verdict = $log | Select-String 'VOICE_IMPORTED|VOICE_IMPORT_FAILED' | Select-Object -Last 1
    Write-Host "   $verdict"
    if (-not $verdict -or "$verdict" -match 'FAILED') { $failed += $key }
}
if ($failed.Count) { throw "Failed: $($failed -join ', ')" }
