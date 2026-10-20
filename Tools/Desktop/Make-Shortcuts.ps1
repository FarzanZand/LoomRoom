# Puts the two LoomRoom shortcuts on the desktop: "Play The Table Waits" and "LoomRoom Asset Studio".
# Both run LoomRoomLaunch.ps1 with no console window. Safe to run again (it replaces them).
$here = $PSScriptRoot
$desktop = [Environment]::GetFolderPath('Desktop')
$shell = New-Object -ComObject WScript.Shell
$ps = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'

function Make($name, $mode, $icon, $description) {
    $link = $shell.CreateShortcut((Join-Path $desktop "$name.lnk"))
    $link.TargetPath = $ps
    $link.Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$here\LoomRoomLaunch.ps1`" -Mode $mode"
    $link.WorkingDirectory = $here
    $link.IconLocation = (Join-Path $here $icon)
    $link.Description = $description
    $link.WindowStyle = 7   # minimized: no console flash
    $link.Save()
    Write-Output (Join-Path $desktop "$name.lnk")
}

Make 'Play The Table Waits' 'Play' 'Play.ico' 'Play the game in the LoomRoom Editor (no build).'
Make 'LoomRoom Asset Studio' 'Studio' 'Asset Studio.ico' 'Open Asset Studio in the LoomRoom Editor.'
