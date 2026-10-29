<#
.SYNOPSIS
  Trusts the Halo development certificate on THIS PC and installs artifacts\msix\Halo-dev.msix (L-F02).

.DESCRIPTION
  Run from an elevated PowerShell ("Run as administrator"): Windows only installs a sideloaded package when its
  signing certificate is in the machine's Trusted People store, and writing there needs admin rights.
  Only the public certificate (.cer) is imported. Undo with uninstall-dev.ps1 -RemoveCertificate.
#>
$ErrorActionPreference = 'Stop'
$out = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\msix'
$cer = Join-Path $out 'ProjectHalo-Dev.cer'
$msix = Join-Path $out 'Halo-dev.msix'
if (-not (Test-Path $msix)) { throw "Build first: packaging\build-msix.ps1" }

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { throw 'Run this script from an elevated PowerShell (Run as administrator).' }

Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
Add-AppxPackage -Path $msix -ForceApplicationShutdown
Get-AppxPackage -Name 'ProjectHalo.Island.Dev' | Select-Object Name, Version, InstallLocation, SignatureKind
