$thumbprint = "5112B19D4500CFF97AE01A9DE4DB81F447EC22EA"

dotnet build .\src\IPAbuyer.csproj `
-c Release /p:WindowsPackageType=MSIX `
/p:GenerateAppxPackageOnBuild=true `
/p:Platform=x64 `
/p:AppxPackageSigningEnabled=true `
/p:PackageCertificateThumbprint=$thumbprint