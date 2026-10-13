<#
.SYNOPSIS
    Builds and packages TabSpool into an MSIX package for local testing or Microsoft Store submission.

.PARAMETER Version
    Package version string (default: 1.0.0.0).

.PARAMETER PackageIdentityName
    The Package Identity Name from Microsoft Partner Center (default: TabSpool).

.PARAMETER Publisher
    The Publisher ID (e.g. CN=XXXX-XXXX-XXXX) from Partner Center. For local testing, a self-signed dev cert is used.

.PARAMETER PublisherDisplayName
    The Publisher Display Name from Partner Center.

.PARAMETER ForStore
    If specified, creates an unsigned package or .msixupload bundle ready for Microsoft Store submission.
#>
param(
    [string]$Version = "1.0.0.0",
    [string]$PackageIdentityName = "TabSpool",
    [string]$Publisher = "",
    [string]$PublisherDisplayName = "TabSpool Developer",
    [switch]$ForStore
)

$ErrorActionPreference = 'Stop'
$rootDir = $PSScriptRoot
$layoutDir = Join-Path $rootDir "PackageLayout"
$binDir = Join-Path $rootDir "bin\Release\net10.0-windows\win-x64\publish"
$outputDir = Join-Path $rootDir "dist"

Write-Host "==> 1. Publishing TabSpool (.NET 10 self-contained win-x64)..." -ForegroundColor Cyan
dotnet publish "$rootDir\TabSpool.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false

if (-not (Test-Path "$binDir\TabSpool.exe")) {
    throw "Publish failed. TabSpool.exe not found at $binDir"
}

Write-Host "==> 2. Preparing PackageLayout..." -ForegroundColor Cyan
# Copy binary to layout
Copy-Item "$binDir\TabSpool.exe" "$layoutDir\TabSpool.exe" -Force

# Resolve makeappx and signtool from Windows Kits
$makeAppx = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe"
if (-not (Test-Path $makeAppx)) {
    $found = Get-ChildItem -Path "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter "makeappx.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { $makeAppx = $found.FullName }
    else { throw "makeappx.exe not found in Windows Kits directory." }
}

$signTool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
if (-not (Test-Path $signTool)) {
    $found = Get-ChildItem -Path "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { $signTool = $found.FullName }
}

if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

# Determine certificate & publisher for manifest
$cert = $null
if (-not $ForStore) {
    $sub = 'CN=TabSpool Local Dev'
    $now = Get-Date
    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
        Where-Object { $_.Subject -eq $sub -and $_.NotAfter -gt $now } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1

    if (-not $cert) {
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $sub -CertStoreLocation Cert:\CurrentUser\My -NotAfter $now.AddYears(5)
    }

    if ([string]::IsNullOrWhiteSpace($Publisher)) {
        $Publisher = $cert.Subject
    }
} else {
    if ([string]::IsNullOrWhiteSpace($Publisher)) {
        Write-Warning "ForStore specified without -Publisher! Please pass -Publisher 'CN=...' matching your Partner Center identity."
    }
}

Write-Host "==> 3. Updating AppxManifest.xml..." -ForegroundColor Cyan
[xml]$manifest = Get-Content "$layoutDir\AppxManifest.xml"
$manifest.Package.Identity.Name = $PackageIdentityName
$manifest.Package.Identity.Version = $Version
if (-not [string]::IsNullOrWhiteSpace($Publisher)) {
    $manifest.Package.Identity.Publisher = $Publisher
}
if (-not [string]::IsNullOrWhiteSpace($PublisherDisplayName)) {
    $manifest.Package.Properties.PublisherDisplayName = $PublisherDisplayName
}
$manifest.Save("$layoutDir\AppxManifest.xml")

$msixPath = Join-Path $outputDir "TabSpool-$Version-x64.msix"
if (Test-Path $msixPath) { Remove-Item $msixPath -Force }

Write-Host "==> 4. Packing MSIX with makeappx..." -ForegroundColor Cyan
& $makeAppx pack /d "$layoutDir" /p "$msixPath" /o

if (-not (Test-Path $msixPath)) {
    throw "Packaging failed. $msixPath was not created."
}
Write-Host "Package created: $msixPath" -ForegroundColor Green

if (-not $ForStore) {
    Write-Host "==> 5. Signing package for local testing..." -ForegroundColor Cyan
    & $signTool sign /fd SHA256 /sha1 $cert.Thumbprint "$msixPath"
    Write-Host "Successfully signed $msixPath for local installation!" -ForegroundColor Green
} else {
    Write-Host "==> Store package ready at: $msixPath" -ForegroundColor Green
    Write-Host "(The Microsoft Store automatically signs your package upon ingestion)."
}
