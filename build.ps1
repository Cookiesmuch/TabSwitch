<#
.SYNOPSIS
    Builds the signed release package: dist\TabSwitch_<version>_x64.msix plus dist\TabSwitch.cer.

.DESCRIPTION
    MSIX packages must be signed. A self-signed certificate (CN=TabSwitch, matching the manifest Publisher)
    is created once in your CurrentUser\My store and reused; only its public .cer is exported to dist.
    Bump Version in src\TabSwitch\Package.appxmanifest before each release.
#>
$ErrorActionPreference = 'Stop'

$dist = Join-Path $PSScriptRoot 'dist'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory $dist | Out-Null

$subject = 'CN=TabSwitch'
$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
    Select-Object -First 1
if (-not $cert) {
    Write-Host "Creating self-signed signing certificate $subject"
    $cert = New-SelfSignedCertificate -Type Custom -Subject $subject -KeyUsage DigitalSignature `
        -FriendlyName 'TabSwitch MSIX signing' -CertStoreLocation Cert:\CurrentUser\My `
        -NotAfter (Get-Date).AddYears(5) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
}
Export-Certificate -Cert $cert -FilePath (Join-Path $dist 'TabSwitch.cer') | Out-Null

# Own output path, so a dev build running from src\TabSwitch\bin doesn't lock the build.
dotnet publish (Join-Path $PSScriptRoot 'src\TabSwitch\TabSwitch.csproj') `
    -c Release -r win-x64 -p:Platform=x64 --self-contained true `
    -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true `
    -p:AppxPackageSigningEnabled=true "-p:PackageCertificateThumbprint=$($cert.Thumbprint)" `
    "-p:BaseOutputPath=$dist\build\" "-p:AppxPackageDir=$dist\packages\"
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

$msix = Get-ChildItem (Join-Path $dist 'packages') -Recurse -Filter *.msix | Select-Object -First 1
if (-not $msix) { throw 'No .msix produced' }
Copy-Item $msix.FullName $dist
Write-Host "Created $(Join-Path $dist $msix.Name) and $(Join-Path $dist 'TabSwitch.cer')"
