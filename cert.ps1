$cert = New-SelfSignedCertificate `
    -Type Custom `
    -Subject "CN=YourCompany, O=YourCompany, C=CN" `
    -KeyUsage DigitalSignature `
    -FriendlyName "IPAbuyer Certificate" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") `
    -NotAfter (Get-Date).AddYears(3)

$cert.Thumbprint

$password = ConvertTo-SecureString -String "YourPassword123" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath ".\IPAbuyer_Certificate.pfx" -Password $password

Export-Certificate -Cert $cert -FilePath ".\IPAbuyer_Certificate.cer"
