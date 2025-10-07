$cert = New-SelfSignedCertificate `
    -Type Custom `
    -Subject "CN=IPAbuyer Publisher" `
    -KeyUsage DigitalSignature `
    -FriendlyName "IPAbuyer Signing Certificate" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") `
    -NotAfter (Get-Date).AddYears(2) `
    -KeyExportPolicy Exportable

$thumbprint = $cert.Thumbprint

dotnet build .\src\IPAbuyer.csproj `
-c Release /p:WindowsPackageType=MSIX `
/p:GenerateAppxPackageOnBuild=true `
/p:Platform=x64 `
/p:AppxPackageSigningEnabled=true `
/p:PackageCertificateThumbprint=$thumbprint