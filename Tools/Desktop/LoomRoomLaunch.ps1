# The desktop shortcuts "Play The Table Awaits" and "LoomRoom Asset Studio".
#   LoomRoomLaunch.ps1 -Mode Play     plays the game in the Editor (Room.unity, the save in use); nothing is built
#   LoomRoomLaunch.ps1 -Mode Studio   opens Asset Studio
# With the LoomRoom Editor already open it asks that Editor (through the Unity CLI) and brings its window to the front;
# otherwise it starts the Editor on the project and the Editor plays (or opens the studio) once it has loaded.
# Make-Shortcuts.ps1 puts the two shortcuts on the desktop.
param([ValidateSet('Play', 'Studio')][string]$Mode = 'Play')

$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$cli = Join-Path $env:LOCALAPPDATA 'Unity\bin\unity.exe'
$version = (Get-Content (Join-Path $project 'ProjectSettings\ProjectVersion.txt') | Where-Object { $_ -like 'm_EditorVersion:*' }) -replace 'm_EditorVersion:\s*', ''
$editor = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
$name = Split-Path $project -Leaf
# No double quotes in it: Windows PowerShell drops them from a native program's arguments.
$call = if ($Mode -eq 'Play') { 'GameLauncher.Play(); return 7;' } else { 'GameLauncher.Studio(); return 7;' }
$method = if ($Mode -eq 'Play') { 'GameLauncher.PlayOnStart' } else { 'GameLauncher.Studio' }

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class LoomWindow {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
}
"@

# The Editor that has this project open: its window title starts with the project folder's name.
$running = Get-Process Unity -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "$name - *" } | Select-Object -First 1

if ($running) {
    $ok = $false
    if (Test-Path $cli) {
        $ErrorActionPreference = 'Continue'
        $out = & $cli command eval --project-path $project $call 2>&1 | Out-String
        $ErrorActionPreference = 'Stop'
        $ok = $LASTEXITCODE -eq 0 -and $out -match 'result.{0,4}7'
    }
    if (-not $ok) {
        Add-Type -AssemblyName PresentationFramework
        [System.Windows.MessageBox]::Show("The LoomRoom Editor is open but didn't answer (it may be compiling or have a dialog up). Try again in a moment.", 'The Table Awaits') | Out-Null
    }
    $h = $running.MainWindowHandle
    if ([LoomWindow]::IsIconic($h)) { [LoomWindow]::ShowWindow($h, 9) | Out-Null }
    [LoomWindow]::SetForegroundWindow($h) | Out-Null
}
elseif (Test-Path $editor) {
    Start-Process -FilePath $editor -ArgumentList @('-projectPath', "`"$project`"", '-executeMethod', $method)
}
else {
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show("Unity $version isn't installed at $editor.", 'The Table Awaits') | Out-Null
}
