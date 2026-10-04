@echo off
echo Trusting "CN=TabSpool Local Dev" code-signing certificate...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -eq 'CN=TabSpool Local Dev' } | Select-Object -First 1; ^
   if (-not $cert) { Write-Error 'Certificate not found. Run build.bat first.'; exit 1 }; ^
   $cer = Join-Path $env:TEMP 'TabSpool-LocalDev.cer'; ^
   Export-Certificate -Cert $cert -FilePath $cer -Force | Out-Null; ^
   Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile -ExecutionPolicy Bypass -Command \"Import-Certificate -FilePath ''' + $cer + ''' -CertStoreLocation Cert:\LocalMachine\Root | Out-Null; Import-Certificate -FilePath ''' + $cer + ''' -CertStoreLocation Cert:\LocalMachine\TrustedPublisher | Out-Null\"'; ^
   Remove-Item $cer -Force -ErrorAction SilentlyContinue; ^
   Write-Host 'Certificate trusted! Re-verifying TabSpool.exe...' -ForegroundColor Green; ^
   .\sign.ps1 '.\TabSpool.exe'"
pause
