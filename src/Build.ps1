param([string]$OutputPath, [switch]$TestBuild, [switch]$Portable)
$ErrorActionPreference = 'Stop'
$appRoot = Split-Path -Parent $PSScriptRoot
if ($TestBuild -and $Portable) { throw 'Choose either -TestBuild or -Portable.' }
if (-not $OutputPath) {
    $fileName = if ($Portable) { 'DesktopAgenda-Portable.exe' } else { 'DesktopAgenda.exe' }
    $OutputPath = Join-Path $appRoot $fileName
}
$iconPath = Join-Path $appRoot 'DesktopAgenda.ico'
if (-not (Test-Path -LiteralPath $iconPath)) { & (Join-Path $PSScriptRoot 'BuildIcon.ps1') -OutputPath $iconPath | Out-Null }
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$compilerArgs = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/codepage:65001',"/out:$OutputPath","/win32manifest:$PSScriptRoot\DesktopAgenda.exe.manifest","/win32icon:$iconPath",'/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll')
if ($TestBuild) { $compilerArgs += '/define:TESTING' }
if ($Portable) { $compilerArgs += '/define:PORTABLE' }
$compilerArgs += @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object { $_.FullName })
& $compilerPath @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output $OutputPath
