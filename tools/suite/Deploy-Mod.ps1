<#
.SYNOPSIS
  The install step of `dotnet build citymaker.csproj -c Release` (the csproj's DeployStandalone target calls it):
  bin\Release + UI\build into Mods\Citymaker, as the user's build.

.DESCRIPTION
  Mods\Citymaker is shared with the game queue (ModsInstall.ps1), so this install is guarded like the queue's own:
  * refused (the build fails, nothing is touched) while any Cities2.exe runs, and while a queue game owns the folder;
  * a copy in a staging folder, then two renames, under the queue's install.lock: never a half-written folder;
  * the result is the user's build: the game queue puts it back after each of its games.
  Build with -p:Deploy=false to skip installing (the game queue takes your build from bin\Release).
#>
param([Parameter(Mandatory = $true)][string]$From, [string]$Ui = '', [Parameter(Mandatory = $true)][string]$To)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ModsInstall.ps1')
$full = { param($p) [IO.Path]::GetFullPath($p).TrimEnd('\') }
$sources = @((& $full $From)) + @(if ($Ui -and (Test-Path -LiteralPath $Ui)) { (& $full $Ui) })
$real = Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\Citymaker'
if ((& $full $To) -ne (& $full $script:MI.Mods) -and (& $full $To) -eq (& $full $real)) {
    Write-Host "Citymaker was NOT installed into $To`: a stand-in game queue (CITYMAKER_GAME_QUEUE_MODS) is set in this shell; it never installs into the real folder"
    exit 1
}
if ((& $full $To) -ne (& $full $script:MI.Mods)) {
    # not the game's folder (a DeployDir of your own): a plain copy, as the csproj always did
    if (Test-Path -LiteralPath $To) { Remove-Item -LiteralPath $To -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $To | Out-Null
    foreach ($s in $sources) { foreach ($c in Get-ChildItem -LiteralPath $s -Force) { Copy-Item -LiteralPath $c.FullName -Destination $To -Recurse -Force } }
    Write-Host "Citymaker deployed (standalone, no Burst) to $To"
    exit 0
}
$allow = 0; if ($env:CITYMAKER_SUITE_LOCK_PID) { [void][int]::TryParse($env:CITYMAKER_SUITE_LOCK_PID, [ref]$allow) }
$r = MI-DeployOnPurpose $sources $allow
if (-not $r.ok) {
    # the csproj turns this line into a build error (CustomErrorRegularExpression)
    Write-Host "Citymaker was NOT installed into $To`: $($r.message)"
    exit 1
}
Write-Host "Citymaker deployed (standalone, no Burst) to $To`: $($r.message) (the game queue puts it back after its own games)"
exit 0
