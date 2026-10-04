# Publish the single-file TL Browser exe into dist\.
# NOTE: keep this file ASCII-only. Windows PowerShell 5.1 decodes .ps1 as ANSI
# when there is no BOM, so non-ASCII bytes here break parsing.
# Params:
#   -NoCompress   publish without single-file compression (bigger, faster start;
#                 only useful for A/B comparison with tools/time_startup.py)
param([switch]$NoCompress)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "src\TLBrowser.csproj"
$out  = Join-Path $root "dist"
$log  = Join-Path $root "_publish.log"

$dotnet = "C:\Program Files\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

# The single-file publish writes dist\TLBrowser.exe in place, so any still-running
# instance makes it fail with a confusing MSB4018 / "being used by another process".
# Kill leftovers and wait until the file is actually writable before publishing.
function Wait-ExeUnlocked([string]$path, [int]$timeoutSec = 40) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (-not (Test-Path $path)) { return $true }
        try {
            $fs = [System.IO.File]::Open($path, 'Open', 'ReadWrite', 'None')
            $fs.Close()
            return $true
        } catch {
            Get-Process TLBrowser -ErrorAction SilentlyContinue |
                Stop-Process -Force -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 600
        }
    }
    return $false
}

$exe = Join-Path $out "TLBrowser.exe"
Get-Process TLBrowser -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
if (-not (Wait-ExeUnlocked $exe)) {
    "ERROR: $exe is still locked by another process; aborting." |
        Tee-Object -FilePath $log
    exit 1
}

$argv = @(
    "publish", $proj,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-o", $out,
    "-v", "minimal"
)
if ($NoCompress) { $argv += @("-p:EnableCompressionInSingleFile=false") }

& $dotnet @argv *>&1 | Tee-Object -FilePath $log
$rc = $LASTEXITCODE

"RC=$rc" | Add-Content -Path $log -Encoding UTF8
if (Test-Path $exe) {
    $mb = [math]::Round((Get-Item $exe).Length / 1MB, 2)
    "EXE=$exe  SIZE=${mb}MB" | Add-Content -Path $log -Encoding UTF8
}

# The WebView2 NuGet package ships IntelliSense XML docs; publish copies them next
# to the exe. They are not needed at runtime, so drop them to keep dist a single file.
# NOTE: piping into Remove-Item directly gets silently blocked in some sandboxes,
# so iterate explicitly with -LiteralPath on the full path.
Get-ChildItem -Path $out -Filter "*.xml" -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }

# Sync the desktop copy so the desktop icon is always the latest build.
# The file name contains Chinese; build it from code points to keep this script
# ASCII-only, because PowerShell 5.1 would garble a literal Chinese name.
# 0x6D4F=liu 0x89C8=lan 0x5668=qi (the three Chinese chars of the app name)
if ($rc -eq 0 -and (Test-Path $exe)) {
    $deskName = "TL " + [char]0x6D4F + [char]0x89C8 + [char]0x5668 + ".exe"
    $deskExe = Join-Path ([Environment]::GetFolderPath('Desktop')) $deskName
    Copy-Item -Path $exe -Destination $deskExe -Force -ErrorAction SilentlyContinue
    "DESKTOP_SYNC=$deskExe" | Add-Content -Path $log -Encoding UTF8
}

exit $rc
