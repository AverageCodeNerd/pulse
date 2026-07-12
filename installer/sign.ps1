<#
.SYNOPSIS
  Code-signs a file for Pulse. No-op (with a warning) when no certificate is
  configured, so unsigned builds still succeed.

.DESCRIPTION
  Two signing methods, selected by environment variables:

  1) PFX file (any OV/EV code-signing certificate):
       $env:PULSE_CERT_PFX  = "C:\path\to\cert.pfx"
       $env:PULSE_CERT_PASS = "pfx-password"

  2) Azure Trusted Signing (recommended — cheapest, good SmartScreen reputation):
       $env:PULSE_TS_DLIB     = "C:\...\Azure.CodeSigning.Dlib.dll"
       $env:PULSE_TS_METADATA = "C:\...\trusted-signing-metadata.json"
     (see SIGNING.md for setup)

  Both use SHA-256 with an RFC-3161 timestamp so signatures stay valid after the
  cert expires.

.EXAMPLE
  ./sign.ps1 -File "..\dist\Pulse-Setup.exe"
#>
param(
  [Parameter(Mandatory = $true)][string]$File
)

$ErrorActionPreference = "Stop"

function Find-SignTool {
  $roots = @("C:\Program Files (x86)\Windows Kits\10\bin", "C:\Program Files\Windows Kits\10\bin")
  Get-ChildItem $roots -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match "\\x64\\" } |
    Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}

$timestamp = "http://timestamp.acs.microsoft.com"   # works for Azure TS; for PFX use the CA's TSA or this
$signtool = Find-SignTool
if (-not $signtool) { Write-Warning "signtool.exe not found (install the Windows SDK 'Signing Tools'). Skipping signing."; return }

if ($env:PULSE_CERT_PFX -and (Test-Path $env:PULSE_CERT_PFX)) {
  Write-Host "Signing $File with PFX certificate..."
  & $signtool sign /fd SHA256 /f $env:PULSE_CERT_PFX /p $env:PULSE_CERT_PASS `
    /tr "http://timestamp.digicert.com" /td SHA256 $File
  if ($LASTEXITCODE -ne 0) { throw "signtool failed ($LASTEXITCODE)" }
}
elseif ($env:PULSE_TS_DLIB -and $env:PULSE_TS_METADATA) {
  Write-Host "Signing $File with Azure Trusted Signing..."
  & $signtool sign /v /fd SHA256 /tr $timestamp /td SHA256 `
    /dlib $env:PULSE_TS_DLIB /dmdf $env:PULSE_TS_METADATA $File
  if ($LASTEXITCODE -ne 0) { throw "signtool failed ($LASTEXITCODE)" }
}
else {
  Write-Warning "No signing certificate configured (set PULSE_CERT_PFX or PULSE_TS_* — see SIGNING.md). Leaving '$File' UNSIGNED."
  return
}

Write-Host "Signed: $File"
