# IdentityServer SSO

OpenID Connect single sign-on for Umbraco 18 backoffice users and members using Windows, Active Directory, Samba AD, or FreeIPA accounts.

![MIT license](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/license-mit.svg)
![.NET 10](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/dotnet-10.svg)
![Umbraco 18](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/umbraco-18.svg)

Works in two modes:
- **Embedded server (default):** Hosts [`AurorisNet.IdentityServer`](https://github.com/auroris/AurorisNet.IdentityServer) in-process for Windows Kerberos/NTLM sign-in (and can also sign in other apps).
- **External server:** Set `IdentityServer:Authority` to point at a standalone `AurorisNet.IdentityServer` instance (Active Directory, Samba AD, or FreeIPA).

Includes automatic backoffice redirect with password fallback on failure, member login routes, startup account/group seeding, and soft retirement of deleted accounts that have signed in. Optionally pair with [`AurorisNet.Umbraco.IdentityServer.Directory`](https://github.com/auroris/AurorisNet.Umbraco.IdentityServer.Directory) to pre-create accounts via **Add from directory**.

## Install

```powershell
dotnet add package AurorisNet.Umbraco.IdentityServer.SSO
```

1. Register in `Program.cs` after `AddComposers()`:

   ```csharp
   using IdentityServer.Sso;

   builder.CreateUmbracoBuilder()
       .AddBackOffice()
       .AddWebsite()
       .AddComposers()
       .AddSingleSignOn()
       .Build();
   ```

2. Enable in `appsettings.json`:

   ```json
   "IdentityServer": {
     "Enabled": true,
     "Name": "Domain",
     "ClientId": "intranet-site"
   }
   ```

Under IIS, enable both Windows Authentication and Anonymous Authentication and run `tools/configure-iis-authentication.ps1` (see [embedded server and IIS guide](https://github.com/auroris/AurorisNet.Umbraco.IdentityServer.SSO/blob/main/docs/EMBEDDED-SERVER.md#iis-windows-authentication)). For an external server, set `IdentityServer:Authority` and register callbacks `/signin-oidc/backoffice` and `/signin-oidc/member`.

## Settings

`AddSingleSignOn()` reads four sections (`IdentityServer`, `EmbeddedIdentityServer`, `BackOfficeLogin`, `Accounts`). Key `IdentityServer` settings:

| Setting | Default | Effect |
|---|---|---|
| `Enabled` | `false` | Enables SSO (`ClientId` required). |
| `Name` | `Identity Server` | Button label and scheme suffix (`Umbraco.<Name>` / `UmbracoMembers.<Name>`). **Do not rename after accounts are linked.** |
| `Authority` | empty | External issuer URL; leave empty to use the embedded Windows server. |
| `ClientId` / `ClientSecret` | empty | OIDC client credentials (`ClientSecret` optional for public/embedded clients). |
| `BackOfficeEnabled` / `MemberEnabled` | `true` | Enable SSO per audience. |
| `DenyLocalLogin` | `false` | Hides the backoffice password form. |
| `BackOfficeAutoLink:AutoLinkExternalAccount` | `false` | Auto-create backoffice users on first sign-in. |
| `MemberAutoLink:AutoLinkExternalAccount` | `true` | Auto-create members on first sign-in. |

All settings across all four sections, login flows, member Razor helpers, account seeding, and retirement rules are in [Configuration and flows](https://github.com/auroris/AurorisNet.Umbraco.IdentityServer.SSO/blob/main/docs/configuration.md).

## Supported versions

- Umbraco 18: <!-- umbraco-releases 18 -->18.0.0 to 18.3.2<!-- /umbraco-releases --> (.NET 10)

## Documentation

- [Configuration, flows, and account rules](https://github.com/auroris/AurorisNet.Umbraco.IdentityServer.SSO/blob/main/docs/configuration.md)
- [Embedded identity server and IIS](https://github.com/auroris/AurorisNet.Umbraco.IdentityServer.SSO/blob/main/docs/EMBEDDED-SERVER.md)
- [Developing and releases](https://github.com/auroris/AurorisNet.Umbraco.IdentityServer.SSO/blob/main/docs/developing.md)
- [Security policy](https://github.com/auroris/AurorisNet.Umbraco.IdentityServer.SSO/blob/main/SECURITY.md)

## License

[MIT](https://github.com/auroris/AurorisNet.Umbraco.IdentityServer.SSO/blob/main/LICENSE)
