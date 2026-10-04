# Read/write a shortcut's arguments and target. Used by verify_guard.py.
# NOTE: this file MUST stay pure ASCII. Windows PowerShell 5.1 decodes .ps1 as
# ANSI when there is no BOM, so any non-ASCII byte here corrupts the parser and
# the script dies with a bogus "unexpected token" error.
param(
    [Parameter(Mandatory = $true, Position = 0)][string]$Path,
    [Parameter(Mandatory = $true, Position = 1)][ValidateSet('get', 'set', 'target', 'show')][string]$Action,
    [Parameter(Position = 2)][string]$Value = ''
)

$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 writes redirected stdout in the system ANSI codepage
# (GBK on this box). Without the line below, a path containing CJK characters
# (e.g. the workspace folder name) reaches the Python caller as mojibake and
# string comparisons silently fail.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

if (-not (Test-Path -LiteralPath $Path)) { Write-Output 'ERR:not-found'; exit 1 }

$sh = New-Object -ComObject WScript.Shell
$sc = $sh.CreateShortcut($Path)

switch ($Action) {
    'get'    { Write-Output $sc.Arguments }
    'set'    { $sc.Arguments = $Value; $sc.Save(); Write-Output 'ok' }
    'target' { Write-Output $sc.TargetPath }
    'show'   {
        Write-Output ("target=" + $sc.TargetPath)
        Write-Output ("args=" + $sc.Arguments)
        Write-Output ("icon=" + $sc.IconLocation)
        Write-Output ("work=" + $sc.WorkingDirectory)
    }
}
