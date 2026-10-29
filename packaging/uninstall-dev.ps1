<#
.SYNOPSIS
  Removes the Halo development package (L-F03). With -RemoveCertificate (elevated), also removes the dev certificate
  from the machine and user Trusted People stores and the signing certificate from the user's Personal store.
  Halo's own data in %LOCALAPPDATA%\ProjectHalo is the user's and is left alone.
#>
param([switch] $RemoveCertificate)
$ErrorActionPreference = 'Stop'
$subject = 'CN=ProjectHalo Dev'

Get-AppxPackage -Name 'ProjectHalo.Island.Dev' | Remove-AppxPackage
if ($RemoveCertificate) {
    foreach ($store in 'Cert:\LocalMachine\TrustedPeople', 'Cert:\CurrentUser\TrustedPeople', 'Cert:\CurrentUser\My') {
        Get-ChildItem $store | Where-Object Subject -eq $subject | Remove-Item
    }
}
if (Get-Process Halo -ErrorAction SilentlyContinue) { Write-Warning 'A Halo process is still running.' } else { 'Halo (dev) removed; no Halo process running.' }
