param(
    [string]$Apk = (Join-Path $PSScriptRoot '../Builds/HapbeatBoxing.apk'),
    [string]$AndroidSdk = 'M:/GameEngine/Unity/Editor/6000.3.12f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK'
)
$ErrorActionPreference = 'Stop'
$apkPath = (Resolve-Path -LiteralPath $Apk).Path
$aaptPath = Get-ChildItem -LiteralPath (Join-Path $AndroidSdk 'build-tools') -Directory |
    Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'aapt.exe' } |
    Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $aaptPath) { throw 'Android SDK aapt.exe was not found.' }
$badging = (& $aaptPath dump badging $apkPath) -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'APK badging failed.' }
$manifest = (& $aaptPath dump xmltree $apkPath 'AndroidManifest.xml') -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'APK manifest dump failed.' }
foreach ($required in @("package: name='com.hapbeat.boxing'", "native-code: 'arm64-v8a'", 'android.permission.INTERNET', 'com.unity3d.player.UnityPlayerGameActivity')) {
    if (-not ($badging + $manifest).Contains($required)) { throw "APK requirement missing: $required" }
}
if (-not $manifest.Contains('quest3s')) { throw 'Quest 3S support is missing.' }
if (-not $manifest.Contains('oculus.software.handtracking')) { throw 'Optional hand tracking feature is missing.' }
if ($badging -match "(?m)^uses-feature: name='[^']*(eye_tracking|handtracking)") { throw 'Optional tracking was marked required.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($apkPath)
try {
    if ($null -eq $zip.GetEntry('lib/arm64-v8a/libil2cpp.so')) { throw 'ARM64 IL2CPP runtime is missing.' }
} finally { $zip.Dispose() }
[pscustomobject]@{
    Result = 'PASS'; Apk = $apkPath; Sha256 = (Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash
    Package = 'com.hapbeat.boxing'; Runtime = 'ARM64 IL2CPP'; Quest3S = $true; HandTrackingOptional = $true
} | ConvertTo-Json
