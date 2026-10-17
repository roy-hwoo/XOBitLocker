# BitLocker Vault - build script
# Compiles with Roslyn csc.exe directly (no .NET SDK / no targeting pack required).
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $root 'src'
$out  = Join-Path $root 'dist'

$csc = 'C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
$fx  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"

if (-not (Test-Path $csc)) { throw "Roslyn csc.exe not found: $csc" }
if (-not (Test-Path $fx))  { throw ".NET Framework runtime dir not found: $fx" }

if (-not (Test-Path $out)) { New-Item -ItemType Directory -Path $out | Out-Null }

$icon     = Join-Path $root 'lock.ico'
$manifest = Join-Path $src  'App.manifest'
$exe      = Join-Path $out  'XOBitLocker.exe'

$sources = Get-ChildItem -Path $src -Filter *.cs -Recurse | ForEach-Object { $_.FullName }

$refNames = @(
  'mscorlib.dll',
  'System.dll',
  'System.Core.dll',
  'System.Drawing.dll',
  'System.Windows.Forms.dll',
  'System.Security.dll',
  'System.Xml.dll',
  'System.Management.dll',
  'System.Web.Extensions.dll'
)
$refs = $refNames | ForEach-Object {
  $p = Join-Path $fx $_
  if (-not (Test-Path $p)) { throw "Missing reference assembly: $p" }
  "/reference:$p"
}

$cscArgs = @(
  '/nologo',
  '/target:winexe',
  '/langversion:7.3',
  '/optimize+',
  '/platform:anycpu',
  "/out:$exe"
)
if (Test-Path $icon)     { $cscArgs += "/win32icon:$icon" }
if (Test-Path $manifest) { $cscArgs += "/win32manifest:$manifest" }
$cscArgs += $refs
$cscArgs += $sources

Write-Host "[BUILD] compiling $($sources.Count) source files ..."
$cscOutput = & $csc $cscArgs 2>&1 | Out-String
if ($cscOutput.Trim().Length -gt 0) { Write-Host $cscOutput }
if ($LASTEXITCODE -ne 0) { throw "[BUILD] FAILED (exit $LASTEXITCODE)" }

$item = Get-Item $exe
Write-Host ("[BUILD] OK -> {0}" -f $item.FullName) -ForegroundColor Green
Write-Host ("[BUILD] size = {0:N0} bytes ({1:N2} KB)" -f $item.Length, ($item.Length / 1KB)) -ForegroundColor Green
