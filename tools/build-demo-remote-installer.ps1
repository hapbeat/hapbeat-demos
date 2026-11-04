# Build the Demo Remote installer for Windows and copy it to installers\DemoRemote-Setup.exe (git-ignored, one file,
# overwritten), then open that folder. One action: double-click build-demo-remote-installer.cmd. Run the installer from
# Explorer; a new one upgrades the installed app in place, with its settings kept.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'android\demo-remote'

function Test-Jdk([string]$dir) { $dir -and (Test-Path (Join-Path $dir 'bin\java.exe')) }

# Gradle's JDK: anything from 17 up (Android Studio's JBR has no jpackage; the build finds a JDK with it by itself).
$candidates = @($env:JAVA_HOME, 'C:\Program Files\Android\Android Studio\jbr') +
    @(Get-ChildItem 'C:\Program Files\Java' -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | ForEach-Object FullName) +
    @(Get-ChildItem (Join-Path $env:USERPROFILE '.gradle\jdks') -Directory -ErrorAction SilentlyContinue | ForEach-Object FullName)
$jdk = $candidates | Where-Object { Test-Jdk $_ } | Select-Object -First 1
if (-not $jdk) { throw 'No JDK found. Install Android Studio or a JDK 17 or later (for example from https://adoptium.net), then run this again.' }
$packaging = @($env:DEMO_REMOTE_JPACKAGE_JDK, $env:JAVA_HOME) +
    @(Get-ChildItem (Join-Path $env:USERPROFILE '.gradle\jdks') -Directory -ErrorAction SilentlyContinue | ForEach-Object FullName) +
    @(Get-ChildItem 'C:\Program Files\Java' -Directory -ErrorAction SilentlyContinue | ForEach-Object FullName)
if (-not ($packaging | Where-Object { $_ -and (Test-Path (Join-Path $_ 'bin\jpackage.exe')) })) {
    throw 'No JDK with jpackage found (needed for the installer). Install a JDK 17 or later, for example from https://adoptium.net, then run this again.'
}
$env:JAVA_HOME = $jdk
Write-Host "Gradle JDK: $jdk"

$dirty = (& git -C $root status --porcelain -- android/demo-remote) -join ''
if ($dirty) {
    Write-Warning ('android/demo-remote has uncommitted changes. The installer version number comes from the commit count, ' +
        'so it does not change: this installer will NOT upgrade an install made from the same commit. Commit first if you need that.')
}

& (Join-Path $project 'gradlew.bat') -p (Join-Path $project 'desktop') packageExe --console=plain
if ($LASTEXITCODE -ne 0) { throw "The build failed (exit code $LASTEXITCODE). See the messages above." }

$built = Get-ChildItem (Join-Path $project 'desktop\build\compose\binaries\main\exe\*.exe') | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $built) { throw 'The build finished but no installer .exe was found under desktop\build\compose\binaries\main\exe.' }

$outDir = Join-Path $root 'installers'
New-Item -ItemType Directory -Force $outDir | Out-Null
$target = Join-Path $outDir 'DemoRemote-Setup.exe'
Copy-Item $built.FullName $target -Force
Write-Host "Installer: $target ($([math]::Round((Get-Item $target).Length / 1MB)) MB, from $($built.Name))"
Start-Process explorer.exe $outDir
