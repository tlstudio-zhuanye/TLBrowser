# Create a desktop shortcut to dist\TLBrowser.exe
# NOTE: keep this file ASCII-only. Windows PowerShell 5.1 reads .ps1 as ANSI
# unless a BOM is present, which corrupts non-ASCII string literals.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe  = Join-Path $root "dist\TLBrowser.exe"
if (-not (Test-Path $exe)) { throw "not found: $exe" }

$desktop = [Environment]::GetFolderPath("Desktop")
# "\u6D4F\u89C8\u5668" = the Chinese word for "browser"; built from code points so
# this file stays pure ASCII and survives ANSI-decoding by PowerShell 5.1.
$cnName  = -join ([char]0x6D4F, [char]0x89C8, [char]0x5668)
$lnk     = Join-Path $desktop ("TL {0}.lnk" -f $cnName)

$sh = New-Object -ComObject WScript.Shell
$sc = $sh.CreateShortcut($lnk)
$sc.TargetPath       = $exe
$sc.WorkingDirectory = Split-Path $exe
$sc.IconLocation     = "$exe,0"
$sc.Description      = "TL Browser"
$sc.Save()

Write-Output "created: $lnk"
