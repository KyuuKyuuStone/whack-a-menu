# Build ContextMenuCleaner.exe using the .NET Framework compiler shipped with Windows.
# No Visual Studio, no NuGet, no runtime install required.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $root 'src'
$dist = Join-Path $root 'dist'
$icon = Join-Path $src 'AppIcon.ico'
$manifest = Join-Path $src 'app.manifest'
$mascot = Join-Path $root 'docs\logo-embed.png'
$out  = Join-Path $dist 'ContextMenuCleaner.exe'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "C# compiler not found: $csc" }
if (-not (Test-Path $icon)) { throw "Missing $icon -- run: python tools/make_icon.py" }

New-Item -ItemType Directory -Force -Path $dist | Out-Null

$files = @(Get-ChildItem -Path $src -Recurse -Filter '*.cs' | ForEach-Object { $_.FullName })
$refs = @('System.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll',
          'System.Core.dll', 'System.Xml.dll')

# The compiler bundled with .NET Framework 4.x is C# 5; the sources stay within
# that syntax so the project builds on a clean Windows machine with zero installs.
$args = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+',
          '/warn:1', '/warnaserror-',
          ('/out:' + $out), ('/win32icon:' + $icon), ('/win32manifest:' + $manifest))
# Full-colour mascot shown at 88px in the About dialog. The 32px title-bar icon
# uses the ICO above, whose small sizes are a tight crop of the character's face.
if (Test-Path $mascot) {
    $args += ('/resource:' + $mascot + ',ContextMenuCleaner.Assets.Mascot.png')
}
foreach ($r in $refs) { $args += ('/r:' + $r) }
$args += $files

Write-Host "Compiling $($files.Count) source files -> $out"
& $csc $args
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE" }

$size = (Get-Item $out).Length
Write-Host ("Done. {0:N0} bytes" -f $size)
