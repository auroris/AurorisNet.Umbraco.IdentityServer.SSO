# Developing

Requires the .NET 10 SDK (`global.json`) and a sibling `../AurorisNet.IdentityServer` checkout (or `-p:UseIdentityServerSource=false` to use NuGet). Release packs require `-p:UseIdentityServerSource=false`. The Windows sign-in test requires Windows.

```powershell
dotnet build -c Release
dotnet test --project tests/IdentityServer.Sso.Tests -c Release
dotnet test --project tests/IdentityServer.Sso.Tests -c Release -p:UmbracoVersion=18.0.0
dotnet pack src/AurorisNet.Umbraco.IdentityServer.SSO.csproj -c Release -o artifacts/packages
```

MSBuild properties: `UseIdentityServerSource` (default `true`) and `IdentityServerRoot` (default `../AurorisNet.IdentityServer`).

Umbraco API touchpoints to check if a new release fails tests: `AddBackOfficeExternalLogins`, `AddMemberExternalLogins`, `BackOfficeLoginController`, `IPackageManifestReader`, `IUserService`, `IMemberEditingService`, Umbraco OpenIddict server events, `UmbracoPipelineFilter`, and `AurorisNet.IdentityServer` client validation handlers.

## Releases

See the shared [release guide](https://github.com/auroris/AurorisNet.Build/blob/main/docs/releasing.md).
