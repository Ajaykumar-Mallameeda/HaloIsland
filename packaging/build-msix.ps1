<#
.SYNOPSIS
  Builds a signed DEVELOPMENT MSIX of Halo (L-F02) into artifacts\msix (ignored by Git).

.DESCRIPTION
  1. dotnet publish src\Halo.App (Release, framework-dependent with the x64 apphost: needs the .NET 10 Desktop
     Runtime). No runtime identifier, so the locked restore files stay valid.
  2. Generates the logo PNGs (a dark pill) so no image binaries live in the repository.
  3. Writes AppxManifest.xml from packaging\Package.appxmanifest with the version.
  4. makeappx pack, then signtool sign with a self-signed code-signing certificate "CN=ProjectHalo Dev" kept in the
     current user's certificate store (created once; no private key is ever written to disk or to the repository).
  5. Exports the public certificate (.cer) next to the package, for trusting it before install.

  Nothing is installed by this script. Use install-dev.ps1 to trust the certificate and install.
#>
[CmdletBinding()]
param(
    [string] $Version = '0.1.0.0',
    [string] $Sdk = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$out = Join-Path $repo 'artifacts\msix'
$layout = Join-Path $out 'layout'
$package = Join-Path $out 'Halo-dev.msix'
$cerPath = Join-Path $out 'ProjectHalo-Dev.cer'
$subject = 'CN=ProjectHalo Dev'

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $layout | Out-Null

# 1. Publish.
& dotnet publish (Join-Path $repo 'src\Halo.App\Halo.App.csproj') -c Release -o $layout -p:DebugType=none | Out-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

# 2. Logos: a dark pill with a light outline, drawn at build time.
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $layout 'Assets'
New-Item -ItemType Directory -Force $assets | Out-Null
foreach ($logo in @(@{ Name = 'Square150x150Logo.png'; Size = 150 }, @{ Name = 'Square44x44Logo.png'; Size = 44 }, @{ Name = 'StoreLogo.png'; Size = 50 })) {
    $s = $logo.Size
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $h = [Math]::Round($s * 0.36); $w = [Math]::Round($s * 0.84); $x = ($s - $w) / 2; $y = ($s - $h) / 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $y, $h, $h, 90, 180); $path.AddArc($x + $w - $h, $y, $h, $h, 270, 180); $path.CloseFigure()
    $g.FillPath([System.Drawing.Brushes]::Black, $path)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(200, 255, 255, 255)), ([Math]::Max(1, $s / 40))
    $g.DrawPath($pen, $path)
    $bmp.Save((Join-Path $assets $logo.Name), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

# 3. Manifest.
(Get-Content (Join-Path $PSScriptRoot 'Package.appxmanifest') -Raw).Replace('{VERSION}', $Version) |
    Set-Content -Encoding utf8 (Join-Path $layout 'AppxManifest.xml')

# 4. Pack and sign.
& (Join-Path $Sdk 'makeappx.exe') pack /d $layout /p $package /o /nv | Out-Host
if ($LASTEXITCODE -ne 0) { throw "makeappx failed ($LASTEXITCODE)" }

$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $subject -and $_.NotAfter -gt (Get-Date) } | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -CertStoreLocation Cert:\CurrentUser\My `
        -KeyUsage DigitalSignature -FriendlyName 'Project Halo development signing (local only)' -NotAfter (Get-Date).AddYears(1) `
        -TextExtension @('2.5.29.19={text}')
}

& (Join-Path $Sdk 'signtool.exe') sign /fd SHA256 /sha1 $cert.Thumbprint /s My $package | Out-Host
if ($LASTEXITCODE -ne 0) { throw "signtool failed ($LASTEXITCODE)" }

# 5. Public certificate only.
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
Write-Host "Package: $package"
Write-Host "Certificate (public): $cerPath  thumbprint $($cert.Thumbprint)"
