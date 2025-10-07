$thumbprint = $cert.Thumbprint

dotnet build .\src\IPAbuyer.csproj `
-c Release /p:WindowsPackageType=MSIX `
/p:GenerateAppxPackageOnBuild=true `
/p:Platform=x64 `
/p:AppxPackageSigningEnabled=true `
/p:PackageCertificateThumbprint=$thumbprint