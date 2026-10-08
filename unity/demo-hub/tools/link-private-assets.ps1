<#
.SYNOPSIS
  Links the Demo Hub to the private assets checkout with a directory junction (idempotent).

.DESCRIPTION
  Creates

    Assets/HapbeatPrivate -> <workspace>/hapbeat-demos/private-assets/unity/demo-hub/HapbeatPrivate

  only when the target exists. The folder holds Meta's OpenXR hand meshes and their baked skin
  textures (Oculus SDK License: inside built apps only, never in this public repository), which the
  shared hands of com.hapbeat.demo-switch load from Resources/HapbeatPrivate/MetaHands. Without the
  link the hub falls back to the package's procedural ghost hands. The link and its .meta are
  git-ignored. Run it with the Unity Editor closed.

.EXAMPLE
  .\tools\link-private-assets.ps1
  .\tools\link-private-assets.ps1 -WorkspaceRoot 'C:\GitHub\Hapbeat\hapbeat-sdk-workspace'
#>
[CmdletBinding()]
param(
    # Default: the parent of this tools/ folder.
    [string] $ProjectPath,

    # Default: the project sits at <workspace>/hapbeat-demos/unity/demo-hub.
    [string] $WorkspaceRoot
)

$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 does not populate $PSScriptRoot inside param() defaults.
if ([string]::IsNullOrWhiteSpace($ProjectPath)) { $ProjectPath = Split-Path -Parent $PSScriptRoot }
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) {
    $WorkspaceRoot = Join-Path $ProjectPath '..\..\..'
}
if (-not (Test-Path -LiteralPath $WorkspaceRoot)) { throw "Workspace root not found: $WorkspaceRoot" }
$WorkspaceRoot = (Resolve-Path -LiteralPath $WorkspaceRoot).Path

$path = Join-Path $ProjectPath 'Assets\HapbeatPrivate'
$relative = 'hapbeat-demos\private-assets\unity\demo-hub\HapbeatPrivate'
$target = Join-Path $WorkspaceRoot $relative

# A git worktree of hapbeat-demos has no private-assets checkout: use the main checkout's workspace.
if (-not (Test-Path -LiteralPath $target)) {
    $common = git -C $ProjectPath rev-parse --path-format=absolute --git-common-dir 2>$null
    if ($LASTEXITCODE -eq 0 -and $common) {
        $mainWorkspace = Split-Path -Parent (Split-Path -Parent $common)
        $candidate = Join-Path $mainWorkspace $relative
        if (Test-Path -LiteralPath $candidate) {
            $WorkspaceRoot = $mainWorkspace
            $target = $candidate
        }
    }
}

Write-Output "link-private-assets"
Write-Output "  project   : $ProjectPath"
Write-Output "  workspace : $WorkspaceRoot"

if (-not (Test-Path -LiteralPath $target)) {
    Write-Output "  skip    Assets\HapbeatPrivate  (target not found: $target; an APK build needs HAPBEAT_DEMO_PROCEDURAL_HANDS=1 for the procedural ghost hands)"
    exit 0
}
$target = (Resolve-Path -LiteralPath $target).Path

$item = Get-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
if ($null -ne $item) {
    if ($item.LinkType -eq 'Junction' -or $item.LinkType -eq 'SymbolicLink') {
        $current = @($item.Target)[0]
        if ($current.TrimEnd('\') -ieq $target.TrimEnd('\')) {
            Write-Output "  ok      Assets\HapbeatPrivate -> $target"
            exit 0
        }
        Write-Output "  WARN    Assets\HapbeatPrivate already links to $current (expected $target); left unchanged"
        exit 1
    }
    Write-Output "  WARN    Assets\HapbeatPrivate exists as a regular $(if ($item.PSIsContainer) { 'directory' } else { 'file' }); move it away and re-run"
    exit 1
}

New-Item -ItemType Junction -Path $path -Target $target | Out-Null
Write-Output "  created Assets\HapbeatPrivate -> $target"
Write-Output "link-private-assets: done (restart the Unity Editor if it was open)"
