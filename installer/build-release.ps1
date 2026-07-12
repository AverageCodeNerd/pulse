<#
.SYNOPSIS
  One-command release build for Pulse: publish -> sign app -> installer -> sign
  installer -> portable zip. Signing is automatic if a certificate is configured
  (see sign.ps1 / SIGNING.md); otherwise it builds unsigned.

.EXAMPLE
  ./build-release.ps1
#>
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "src\Pulse\Pulse.csproj"
$pub  = Join-Path $root "src\Pulse\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish"
$dist = Join-Path $root "dist"
$iscc = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"
$sign = Join-Path $PSScriptRoot "sign.ps1"

Write-Host "==> Publishing (self-contained)..."
dotnet publish $proj -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true | Out-Null

Write-Host "==> Signing the app executable..."
& $sign -File (Join-Path $pub "Pulse.exe")

Write-Host "==> Building the installer..."
& $iscc (Join-Path $PSScriptRoot "Pulse.iss") | Out-Null

Write-Host "==> Signing the installer..."
& $sign -File (Join-Path $dist "Pulse-Setup.exe")

Write-Host "==> Packaging portable zip..."
$zip = Join-Path $dist "Pulse-portable-x64.zip"
if (Test-Path $zip) { [System.IO.File]::Delete($zip) }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($pub, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host "==> Done. Artifacts in $dist"
Get-ChildItem $dist | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}} | Format-Table -AutoSize
