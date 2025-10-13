$cert = New-SelfSignedCertificate `
    -Type Custom `
    -Subject "CN=68F867E4-B304-4B5D-9818-31B1910E0771" `
    -KeyUsage DigitalSignature `
    -FriendlyName "IPAbuyer Signing Certificate" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") `
    -NotAfter (Get-Date).AddYears(2) `
    -KeyExportPolicy Exportable

Export-Certificate -Cert $cert -FilePath ".\cert.cer" -Type CERT

$Password = ConvertTo-SecureString -String " " -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath ".\cert.pfx" -Password $Password

$pfxBytes = [IO.File]::ReadAllBytes(".\cert.pfx")
$pfxBase64 = [Convert]::ToBase64String($pfxBytes)
$pfxBase64 | Out-File ".\cert.pfx.txt" -Encoding ASCII -NoNewline
