param(
    [string[]]$FilePaths = @(".\TabSpool.exe")
)

$ErrorActionPreference = "Stop"
$certSubject = "CN=TabSpool Local Dev"

# 1. Find or create the self-signed code signing certificate in CurrentUser\My
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object { $_.Subject -eq $certSubject -and $_.NotAfter -gt (Get-Date) } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if (-not $cert) {
    Write-Host "Creating self-signed code signing certificate ($certSubject)..." -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $certSubject `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddYears(10)
}

# 2. Ensure the certificate is in CurrentUser\TrustedPublisher (no prompt required)
if (-not (Test-Path "Cert:\CurrentUser\TrustedPublisher\$($cert.Thumbprint)")) {
    $pubStore = New-Object System.Security.Cryptography.X509Certificates.X509Store("TrustedPublisher", "CurrentUser")
    $pubStore.Open("ReadWrite")
    $pubStore.Add($cert)
    $pubStore.Close()
}

# 3. Sign target executable(s)
$needsTrust = $false
foreach ($path in $FilePaths) {
    if (Test-Path $path) {
        $resolved = (Resolve-Path $path).Path
        Unblock-File -Path $resolved -ErrorAction SilentlyContinue

        $sig = Set-AuthenticodeSignature -FilePath $resolved -Certificate $cert
        if ($sig.Status -eq "Valid") {
            Write-Host "Signed: $resolved [Valid]" -ForegroundColor Green
        } elseif ($sig.SignerCertificate -ne $null) {
            Write-Host "Signed: $resolved [$($sig.Status) - run trust-cert.bat once as Admin to trust root]" -ForegroundColor Yellow
            $needsTrust = $true
        } else {
            throw "Signing failed for '$resolved': $($sig.Status) - $($sig.StatusMessage)"
        }
    }
}

if ($needsTrust) {
    Write-Host ""
    Write-Host "NOTE: The executable is signed, but the certificate is not yet in Trusted Root." -ForegroundColor Yellow
    Write-Host "      Run 'trust-cert.bat' once to finish trusting the certificate on this PC." -ForegroundColor Yellow
}
