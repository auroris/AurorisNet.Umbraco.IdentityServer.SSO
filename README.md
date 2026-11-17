# Umbraco IdentityServer SSO

`AurorisNet.Umbraco.IdentityServer.SSO` signs people in to an Umbraco 18 site with their Windows
account, through OpenID Connect. Backoffice users and website members sign in through the site's
own embedded identity server or through an external one. The package is the Umbraco client of
[`AurorisNet.IdentityServer`](https://www.nuget.org/packages/AurorisNet.IdentityServer), the
OpenID Connect server library.

- **One provider for the backoffice and for members.** The `IdentityServer` settings register an
  OpenID Connect login for each audience. Umbraco links, creates and signs in the accounts with
  its own external-login managers, including its two-factor checks.
- **An embedded identity server.** With `Authority` empty, the site hosts IdentityServer.Core in its
  own process at `/connect/authorize` and `/connect/token`. It authenticates Windows accounts with
  Kerberos or NTLM and signs in other applications listed as its clients. With an `Authority`,
  the site signs in through that external server.
- **Password fallback.** When the identity server is unreachable or refuses someone, the
  backoffice and member pages return to Umbraco's password form, keeping the page and the
  authorization request.
- **Login flow.** `BackOfficeLogin` chooses whether the backoffice login screen redirects to the
  identity server at once or shows the password form first.
- **Account policy.** The `Accounts` section creates listed groups, users and members at
  startup. Deleting a backoffice user or member who has signed in retires the account, so its
  name stays on everything it authored and its username is free for a new person.

The package targets .NET 10 and is licensed under the
[MIT license](https://github.com/auroris/AurorisNet.Umbraco.IdentityServer.SSO/blob/main/LICENSE).
The [embedded server and IIS reference](https://github.com/auroris/AurorisNet.Umbraco.IdentityServer.SSO/blob/main/docs/EMBEDDED-SERVER.md)
covers client registration, claims, keys, IIS setup and troubleshooting.

## Install

1. Add the package to a website running Umbraco 18:

   ```xml
   <PackageReference Include="AurorisNet.Umbraco.IdentityServer.SSO" Version="1.*" />
   ```

2. Register it after the composers in `Program.cs`, so Umbraco's own services exist before the
   package decorates them:

   ```csharp
   using IdentityServer.Sso;

   builder.CreateUmbracoBuilder()
       .AddBackOffice()
       .AddWebsite()
       .AddComposers()
       .AddSingleSignOn()
       .Build();
   ```

3. Turn the provider on in `appsettings.json`:

   ```json
   "IdentityServer": {
     "Enabled": true,
     "Name": "Domain",
     "ClientId": "intranet-site"
   }
   ```

4. Start the site and open `/umbraco`. The login screen redirects to the embedded identity
   server, which signs in the current Windows account. Link that identity to a backoffice user
   first, or set `IdentityServer:BackOfficeAutoLink:AutoLinkExternalAccount` to `true` to create
   users at their first sign-in.

Under IIS, enable **Windows Authentication** and **Anonymous Authentication** for the site, then
run the helper script from the package's `tools` folder, as the
[IIS section of the reference](https://github.com/auroris/AurorisNet.Umbraco.IdentityServer.SSO/blob/main/docs/EMBEDDED-SERVER.md#iis-windows-authentication)
describes. On Kestrel the site performs the Windows exchange itself.

To sign in through an external server, set `IdentityServer:Authority` to its issuer address and
register this site there with the callbacks `/signin-oidc/backoffice` and `/signin-oidc/member`.

## Settings

`AddSingleSignOn()` reads four sections. It registers the providers while services are built, so
a change to these settings takes effect at the next restart; `RetireDeletedUsers` and
`RetireDeletedMembers` apply at the next delete. Each default below is the package's own, and a
setting left out of the configuration keeps it.

### IdentityServer

| Setting | Default | Effect |
|---|---|---|
| `Enabled` | `false` | Registers the provider. `ClientId` is then required. |
| `Name` | `Identity Server` | Button label. Its letters and digits form the schemes (`Domain` gives `Umbraco.Domain` and `UmbracoMembers.Domain`), which stored links name, so keep it once accounts are linked. |
| `Authority` | empty | Empty signs in through the embedded server. Otherwise the external server's issuer address. |
| `ClientId`, `ClientSecret` | empty | This site's client at the identity server. The embedded server admits the site's own client without registration. |
| `BackOfficeEnabled`, `MemberEnabled` | `true` | Offers the provider to each audience. |
| `Scopes` | `openid profile email roles` | Scopes requested. |
| `CallbackPath` | `/signin-oidc` | Callback prefix; `/backoffice` or `/member` follows it. The package reserves it from content routing. |
| `DenyLocalLogin` | `false` | Hides the backoffice password form. |
| `RequireHttpsMetadata` | `true` | Refuses an `http://` external authority. The embedded server's metadata is read in process. |
| `AcceptAnyServerCertificate` | `false` | Skips certificate checks on the site's calls to the identity server (discovery, keys, token). For an isolated intranet only. |
| `GetClaimsFromUserInfoEndpoint` | `false` | Also reads claims from the userinfo endpoint of an external server. |
| `DirectoryApiKey` | empty | Bound for a host that creates accounts from an external server's directory API; the sign-in leaves it unused. |
| `BackOfficeAutoLink:AutoLinkExternalAccount` | `false` | Creates a backoffice user at a first sign-in. |
| `BackOfficeAutoLink:DefaultUserGroups`, `DefaultCulture` | empty | Group aliases and culture of users created that way. |
| `BackOfficeAutoLink:AllowManualLinking` | `true` | Lets a user link the provider from their profile. |
| `MemberAutoLink:AutoLinkExternalAccount` | `true` | Creates a member at a first sign-in. |
| `MemberAutoLink:DefaultMemberTypeAlias` | `Member` | Member type of members created that way. |
| `MemberAutoLink:DefaultIsApproved`, `DefaultMemberGroups`, `DefaultCulture`, `ExternalOnly` | `true`, empty, empty, `false` | Approval, groups, culture and storage of members created that way. |

### EmbeddedIdentityServer

| Setting | Default | Effect |
|---|---|---|
| `Enabled` | automatic | Serves the embedded server. Left out, it runs when this site signs in through it or `Clients` lists an application. |
| `Clients` | empty | Other applications that sign in here: `ClientId`, `ClientSecret`, `RedirectUris`, `DisplayName`. |
| `Groups` | empty | Regular expressions that select the Active Directory groups sent as roles. Empty sends none. |
| `PersistKeys` | `true` | Keeps signing and encryption keys across restarts. |
| `DataPath` | `App_Data/keys/identity-server` | Key folder, relative to the content root. |
| `AccessTokenLifetime`, `IdentityTokenLifetime`, `AuthorizationCodeLifetime` | the server's | Positive lifetimes for the embedded server's clients, such as `01:00:00`. |

### BackOfficeLogin

| Setting | Default | Effect |
|---|---|---|
| `DefaultFlow` | `Auto` | The first thing the login screen does outside debug mode: `Auto`, `Form`, `IdentityServer` or `Debug`, as the backoffice login table below describes. |
| `DebugFlow` | `Debug` | The flow while `Umbraco:CMS:Hosting:Debug` is true. `null` uses `DefaultFlow`. |

### Accounts

| Setting | Default | Effect |
|---|---|---|
| `EnsureMemberGroups`, `EnsureUserGroups` | empty | Groups created at startup when missing. |
| `EnsureBackOfficeUsers`, `EnsureMembers` | empty | Accounts created at startup when missing: `Email`, `Name`, and `UserGroups` or `MemberGroups`. |
| `MemberTypeAlias` | `Member` | Member type of members created from `EnsureMembers` or by `AccountProvisioning`. |
| `RetireDeletedUsers`, `RetireDeletedMembers` | `true` | Retires a deleted account that has signed in or is linked, in place of deleting it. |
| `SeedCurrentWindowsUserAsAdministrator` | `false` | Development: creates `<Windows user>@localhost` as an administrator. |
| `BackOfficeUsersManagedBy`, `MembersManagedBy` | `Umbraco` | `AccountProvisioning` policy: `Umbraco` links to accounts an administrator created; `ActiveDirectory` also creates accounts from managed groups. |
| `ActiveDirectoryGroupPattern` | empty | Regular expression selecting the Umbraco groups that `ActiveDirectory` mode manages. Empty manages every group. An invalid pattern stops startup. |
| `LinkByUserNameOnFirstSignIn` | `true` | Lets `AccountProvisioning` match an unlinked account by email or username. |

## Sign-in flows

Both identity server mechanisms use the same OpenID Connect handler, registered for each audience
under the schemes `Umbraco.<Name>` and `UmbracoMembers.<Name>` with the callbacks
`/signin-oidc/backoffice` and `/signin-oidc/member`. The handler uses the authorization code flow
with a `GET` callback and `SameSite=Lax` cookies. Browsers send Lax cookies on a top-level
navigation from another site, so the identity server can live on any host and the site may run
over plain HTTP; the correlation and nonce cookies are marked `Secure` over HTTPS.

The handler validates the protocol and passes the principal to Umbraco, which finds the stored
link, creates or links the account when its auto-link options allow, and runs its two-factor
checks. The site reads these claims: `sub` (the stable key, a Windows SID from the embedded
server), `name`, the Windows account name, `email`, and one role per directory group. An external
server that sends the same claims works as the embedded one does.

### Backoffice login

The login screen lists the providers from a manifest the package builds from the settings, so the
button follows the configured `Name`.

| Flow | Behavior |
|---|---|
| `Auto` | Redirects to the identity server when its backoffice provider is enabled and can sign people in here; shows the password form otherwise. |
| `Form` | Shows the password form, with the provider button beside it. |
| `IdentityServer` | Redirects to the identity server. Startup stops with a configuration error when the provider is off. |
| `Debug` | Shows the password form and never redirects. Outside debug mode it acts as `Form`. |

The automatic redirect applies to the first sign-in; after logout or a session timeout, Umbraco
shows its provider choice. Another package can call `BackOfficeLoginSettings.GetEffectiveFlow` to
decide its own debug sign-in.

### Fallback to the password form

A failure on the way to the identity server or back returns the browser to
`/umbraco/login?ReturnUrl=...&sso-denied=<reason>`, which shows the password form with the reason:

1. The handler reads the server's metadata before it redirects. When that fails, it redirects to
   the password form.
2. When the server returns an error or the correlation cookie is lost, the handler's remote
   failure event does the same.
3. When Umbraco cannot sign in an external identity because no account is linked, it opens the
   password form with `identity_provider` still in its return address. The package removes that
   parameter, so a valid password continues the original authorization request.

The return address keeps the request's `state` and PKCE `code_challenge`, so the browser still
holds the matching verifier and the backoffice completes the sign-in. A return address that is
missing, points at another host, has a fragment or names another endpoint opens `/umbraco/logout`,
which shows the provider choice without an automatic attempt. Both paths work beneath a virtual
path.

### Member sign-in

`MemberLoginController` starts a member provider from any page and returns there afterwards:

```cshtml
@using IdentityServer.Sso
<a href="@SsoLoginUrls.SurfaceAction(Url, nameof(MemberLoginController.Login), "MemberLogin",
    new { provider = "UmbracoMembers.Domain", returnUrl = Context.Request.Path + Context.Request.QueryString })">Sign in</a>
```

`Login` answers a link, and `Retry` a posted form with Umbraco's anti-forgery and route checks.
Both use Umbraco's own callback, so two-factor sign-in works as it does for a password. A provider
that is not offered, or a server the site cannot reach, returns the browser to the page with
`sso-local=1` and `sso-denied=<reason>` added. A sign-in view shows the password form when it sees
`sso-local`, and offers a retry. `SsoLoginUrls.CleanReturnUrl` accepts only local addresses and
removes those markers after a successful sign-in.

`SsoProviderCatalog` lists the enabled providers. Its `BackOfficeLogins` and `MemberLogins` are
the ones that can sign someone in where the site runs: the embedded server needs Windows
authentication, and an external server authenticates people itself.

## The embedded identity server

The embedded server hosts IdentityServer.Core with its own service container, authentication
schemes, keys and OpenIddict options. It answers discovery at
`/.well-known/openid-configuration`, keys at `/.well-known/jwks`, Windows sign-in at
`/connect/authorize` (authorization code with PKCE `S256`) and code redemption at
`/connect/token`. The issuer is the address of the request, so applications use the address the
site is published under.

The site's own client signs in without registration, at its enabled callback on the exact scheme,
host, port and virtual path of the request. Other applications are listed in
`EmbeddedIdentityServer:Clients`:

```json
"EmbeddedIdentityServer": {
  "Clients": [
    {
      "ClientId": "reports-portal",
      "ClientSecret": "a long random secret",
      "RedirectUris": [ "https://reports.example/signin-oidc" ]
    }
  ],
  "Groups": [ "^Report ", "^Domain Admins$" ]
}
```

Every client uses PKCE and an exact redirect address. The server issues no refresh tokens, so a
client starts a new sign-in when it needs fresh tokens. The embedded server and Umbraco's own
OpenIddict server refuse each other's clients and access tokens. The
[reference](https://github.com/auroris/AurorisNet.Umbraco.IdentityServer.SSO/blob/main/docs/EMBEDDED-SERVER.md)
lists the client rules, the claims for each scope, key storage and the startup checks.

## Who gets an account

**At sign-in.** Umbraco's external-login managers create and link accounts from the
`BackOfficeAutoLink` and `MemberAutoLink` settings. Backoffice auto-linking starts off: an
administrator creates the user and links the identity, or the user links the provider from their
profile after a password sign-in. Member auto-linking starts on. With auto-linking on, Umbraco
matches an existing account by email or creates one from the `email` and `name` claims. Sites
customize the named `BackOfficeExternalLoginProviderOptions` and `MemberExternalLoginProviderOptions`,
including `OnAutoLinking` and `OnExternalLogin`, through Umbraco's options pipeline.

**Configured accounts.** Once the site runs, `ConfiguredAccounts` creates what the `Accounts`
section lists, in this order, and leaves existing groups and accounts as they are:

1. member groups from `EnsureMemberGroups`;
2. members from `EnsureMembers`, approved, with the email as username;
3. user groups from `EnsureUserGroups`, with the alias derived from the name and access to the
   Content section;
4. backoffice users from `EnsureBackOfficeUsers`, and the developer's own Windows account when
   `SeedCurrentWindowsUserAsAdministrator` is true.

The site logs a step that fails and runs the next one. These accounts get a random password;
administrators set a usable one in **Users** and **Members**. A host that imports content naming
these groups before the site starts calls `ConfiguredAccounts.EnsureGroupsAsync()` first.

**Retirement.** Delete in the Users and Members sections retires an account that has signed in or
is linked to an external login, which keeps the person's name on everything that refers to it:

- its username becomes the SID it was linked to, or `retired-` and the account key, and its email
  becomes that username at `retired.invalid`, a reserved domain that delivers nothing;
- it is disabled, its security stamp changes, and its backoffice tokens are revoked;
- its name, groups, start nodes, history and external links stay, and an audit entry records the
  username and email it had.

Umbraco then finds the disabled account for that identity and refuses the sign-in, and a new
person can use the freed username and email. To restore the person, enable the account and give
back its username and email. Accounts that never signed in and have no link are deleted as
Umbraco deletes them, and the built-in administrator is never retired. `RetireDeletedUsers` and
`RetireDeletedMembers` set to `false` return Delete to Umbraco's own behavior.

**Accounts from directory facts.** `AccountProvisioning` creates and links accounts for code that
holds a person's directory facts as an `SsoAccount`, such as a directory picker. OpenID Connect
sign-in leaves it unused.

- An account belongs to the Windows SID that first signed in to it. The SID is the external
  login's provider key under every scheme, so a second provider adds a link to the same account.
- With `LinkByUserNameOnFirstSignIn`, an unlinked account is matched by email or username,
  ignoring case. An account linked to another SID is refused, because Active Directory may give a
  new person the username of someone it deleted.
- With `BackOfficeUsersManagedBy` or `MembersManagedBy` set to `ActiveDirectory`, a first sign-in
  creates an account when a directory group has a managed Umbraco group of the same name, and
  managed memberships follow the directory at every sign-in.
- `LinkAsync` links an account an administrator created to a SID.

## Developing

The source is in `src`, in the namespace `IdentityServer.Sso` and the assembly
`IdentityServer.Sso.dll`; `src/Embedded` holds the in-process host for IdentityServer.Core.
Development needs the .NET 10 SDK (`global.json` pins the SDK and selects the Microsoft Testing
Platform runner), Windows for the Windows sign-in test, and a checkout of the IdentityServer
repository beside this one at `..\IdentityServer`.

A development build compiles `AurorisNet.IdentityServer` from that checkout, so a site that builds
other packages from the same checkout loads one `ActiveDirectory` assembly:

| Property | Default | Effect |
|---|---|---|
| `UseIdentityServerSource` | `true` | `true` references `$(IdentityServerRoot)\IdentityServer.Core\IdentityServer.Core.csproj`; `false` references the `AurorisNet.IdentityServer` package at the version in `Directory.Packages.props`. |
| `IdentityServerRoot` | `..\IdentityServer` beside this repository | The IdentityServer checkout to build from. |

Build, test and pack:

```powershell
dotnet build -c Release
dotnet test --project tests/IdentityServer.Sso.Tests -c Release
dotnet pack src/AurorisNet.Umbraco.IdentityServer.SSO.csproj -c Release -o artifacts/packages
```

The tests boot real sites with SQLite and Kestrel through `AddSingleSignOn()`, one at a time
because Umbraco keeps static state, and finish in under a minute. They cover provider
registration, the embedded server's metadata and client rules, a Windows sign-in with token
redemption, the password fallback, login-flow validation, configured accounts, provisioning and
retirement. The Windows sign-in uses the account running the tests, which Negotiate authenticates
with NTLM on the loopback address. A local package is numbered `0.0.0-local` and contains
`IdentityServer.Sso.dll` with embedded symbols and XML documentation,
`tools/configure-iis-authentication.ps1`, this README, the icon and the licence.

The package compiles against `UmbracoMinimumVersion` in `Directory.Build.props` and allows any
Umbraco 18 release from there; the tests build against `UmbracoVersion`. To test the oldest
supported release, build with `-p:UmbracoVersion=18.0.0`, run the tests with `--no-build`, then
build again without the property. A minor Umbraco release needs a new package version only when
it changes `AddBackOfficeExternalLogins`, `AddMemberExternalLogins`, the auto-link option classes,
`BackOfficeLoginController`, `IPackageManifestReader`, `IUserService`, `IMemberEditingService`,
Umbraco's OpenIddict server events or `UmbracoPipelineFilter`. An update of
`AurorisNet.IdentityServer` needs the tests too: the embedded host removes IdentityServer.Core's
client validation handlers and stops at startup when their number changes.

Keep the build free of warnings: the release workflow builds with `ContinuousIntegrationBuild`,
which turns warnings into errors, except NuGet's vulnerability reports. `docs/images/icon.svg` is
the source of `icon.png`; render it at 128 by 128 pixels after changing it.

### Publish a release

Releases use semantic versions: a fix raises the patch number, a new setting or behavior the
minor number, and a change a site must act on, such as a renamed setting or a new Umbraco major
version, the major number.

Run **Actions → Release → Run workflow** on `main` with the version, such as `1.0.1`. The
workflow checks the version, runs the tests, packs against the published `AurorisNet.IdentityServer`,
reserves the tag `v<version>`, pushes the package to NuGet with trusted publishing as
`AurorisNet`, and creates the GitHub release with the package attached. `dry-run` stops after
packing and keeps the package as an artifact for seven days. A failed run can be started again
with the same version, and a version such as `1.1.0-rc.1` is published as a prerelease.

A `ContinuousIntegrationBuild` pack stops with an error unless `UseIdentityServerSource` is
`false`, because a pack from source would depend on an identity server version nobody published.
Trusted publishing needs a policy on nuget.org for the `AurorisNet` account that names this
repository and `release.yml`. The workflow pins each action to a commit hash, with its version in
a comment, and Dependabot proposes updates.

## Links

- [Embedded server and IIS reference](https://github.com/auroris/AurorisNet.Umbraco.IdentityServer.SSO/blob/main/docs/EMBEDDED-SERVER.md)
- [Source and issues](https://github.com/auroris/AurorisNet.Umbraco.IdentityServer.SSO)
- [Security policy](https://github.com/auroris/AurorisNet.Umbraco.IdentityServer.SSO/blob/main/SECURITY.md):
  reporting a vulnerability, and the trust the site's configuration decides.
