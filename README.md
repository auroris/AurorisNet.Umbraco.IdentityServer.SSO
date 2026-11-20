# Umbraco IdentityServer SSO

> **Warning: additional packages and configuration required.** This package is part of a
> larger identity and single sign-on implementation. It requires:
>
> - A supported Umbraco 18 website running on .NET 10.
> - [`AurorisNet.IdentityServer`](https://github.com/auroris/AurorisNet.IdentityServer/blob/main/IdentityServer.Core/README.md)
>   and `AurorisNet.ActiveDirectory`, installed automatically as NuGet dependencies.
> - A configured identity server: the embedded server with Windows authentication, or a
>   separately deployed server with directory authentication and this site's client and
>   callback addresses registered.
> - `AddSingleSignOn()` registration, enabled `IdentityServer` settings, and an account-linking
>   or provisioning policy for the site's backoffice users and members.
>
> Install and configure
> [`AurorisNet.Umbraco.IdentityServer.Directory`](https://github.com/auroris/AurorisNet.Umbraco.IdentityServer.Directory)
> as well if administrators need to create accounts with **Add from directory**. That
> companion package is optional for sign-in. Installing this package alone does not configure
> the identity server, Windows authentication or account access.

Sign people in to an Umbraco site, as backoffice users and as members, with their Windows,
Active Directory, Samba AD or FreeIPA account.

![MIT license](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/license-mit.svg)
![.NET 10](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/dotnet-10.svg)
![Umbraco 18](https://raw.githubusercontent.com/auroris/AurorisNet.Build/main/badges/umbraco-18.svg)

The package is the Umbraco client of
[AurorisNet.IdentityServer](https://github.com/auroris/AurorisNet.IdentityServer), an OpenID
Connect server that authenticates directory accounts. The site signs in through one of two
servers:

- **An external identity server**, which authenticates Windows, Active Directory, Samba AD
  or FreeIPA accounts. Set `IdentityServer:Authority` to its address.
- **An embedded identity server**, which the site hosts in its own process when `Authority`
  is empty. It authenticates Windows accounts with Kerberos or NTLM, and can sign in other
  applications as well.

The package registers:

- **One login provider for the backoffice and one for members.** Umbraco links, creates and
  signs in the accounts with its own external-login managers, including its two-factor
  checks.
- **A password fallback.** When the identity server is unreachable or the sign-in fails,
  the browser returns to the password form, and the page or authorization request continues
  from there.
- **A login flow.** `BackOfficeLogin` decides whether the backoffice login screen redirects
  to the identity server at once or shows the password form first.
- **Account rules.** The `Accounts` section creates listed groups, users and members at
  startup. Deleting a user or member who has signed in retires the account: its name stays
  on everything it authored, and its username becomes free for a new person.

## Install

1. Add the package to a website that runs a supported Umbraco release:

   ```powershell
   dotnet add package AurorisNet.Umbraco.IdentityServer.SSO
   ```

2. Register it in `Program.cs`, after `AddComposers()`. The package decorates Umbraco's
   user and member services, which must be registered by then:

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
   server, which signs in the current Windows account. While `Umbraco:CMS:Hosting:Debug` is
   true, the login screen shows the password form with a **Domain** button.

A backoffice sign-in needs a user that is linked to the person. Create the user with
[Umbraco Add from Directory](https://github.com/auroris/AurorisNet.Umbraco.IdentityServer.Directory),
let the user link the provider from their profile, or set
`IdentityServer:BackOfficeAutoLink:AutoLinkExternalAccount` to `true` to create users at
their first sign-in.

Under IIS, enable **Windows Authentication** and **Anonymous Authentication** for the site,
then run the helper script from the package's `tools` folder, as
[IIS Windows authentication](https://github.com/auroris/AurorisNet.Umbraco.IdentityServer.SSO/blob/main/docs/EMBEDDED-SERVER.md#iis-windows-authentication)
describes. On Kestrel, the site performs the Windows exchange itself.

To sign in through an external server, set `IdentityServer:Authority` to its issuer address,
and register this site there with the callbacks `/signin-oidc/backoffice` and
`/signin-oidc/member`.

## Settings

`AddSingleSignOn()` reads four sections. A change takes effect at the next restart, except
`RetireDeletedUsers` and `RetireDeletedMembers`, which apply at the next delete.

### IdentityServer

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `false` | Registers the provider. `ClientId` is then required. |
| `Name` | `Identity Server` | The button label. Its letters and digits form the scheme names: `Domain` gives `Umbraco.Domain` and `UmbracoMembers.Domain`. **Stored account links name the scheme, so keep the name once accounts are linked.** |
| `Authority` | empty | The external server's issuer address. Empty signs in through the embedded server. |
| `ClientId` | empty | This site's client id at the identity server. The embedded server admits it without registration. |
| `ClientSecret` | empty | The client secret, for a server that registers this site as a confidential client. |
| `Scopes` | empty | The scopes to request. Empty requests `openid profile email roles`. |
| `CallbackPath` | `/signin-oidc` | The callback prefix, which `/backoffice` or `/member` follows. The package reserves it from content routing. |
| `BackOfficeEnabled` | `true` | Offers the provider on the backoffice login screen. |
| `MemberEnabled` | `true` | Offers the provider to members. |
| `DenyLocalLogin` | `false` | Hides the backoffice password form. |
| `RequireHttpsMetadata` | `true` | Refuses an `http://` authority. The embedded server's metadata is read in process, so the setting has no effect there. |
| `AcceptAnyServerCertificate` | `false` | Skips certificate checks on the site's calls to the identity server for discovery, keys and tokens. **Use it only on an isolated intranet with a self-signed certificate.** |
| `GetClaimsFromUserInfoEndpoint` | `false` | Also reads claims from the server's userinfo endpoint. |
| `DirectoryApiKey` | empty | Bound for a host that searches an external server's directory API. The package itself leaves it unused. |
| `BackOfficeAutoLink:AutoLinkExternalAccount` | `false` | Creates a backoffice user at a person's first sign-in. |
| `BackOfficeAutoLink:DefaultUserGroups` | empty | The user group aliases of users created that way. |
| `BackOfficeAutoLink:DefaultCulture` | unset | The culture of users created that way. Unset uses Umbraco's default. |
| `BackOfficeAutoLink:AllowManualLinking` | `true` | Lets a signed-in user link the provider from their profile. |
| `MemberAutoLink:AutoLinkExternalAccount` | `true` | Creates a member at a person's first sign-in. |
| `MemberAutoLink:DefaultIsApproved` | `true` | Approves members created that way. |
| `MemberAutoLink:DefaultMemberTypeAlias` | `Member` | The member type of members created that way. |
| `MemberAutoLink:DefaultCulture` | unset | The culture of members created that way. Unset uses Umbraco's default. |
| `MemberAutoLink:DefaultMemberGroups` | empty | The member groups that members created that way join. |
| `MemberAutoLink:ExternalOnly` | `false` | Creates members that sign in through the provider only. |

### EmbeddedIdentityServer

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | unset | Serves the embedded server. Unset, the server runs when this site signs in through it or `Clients` lists an application. |
| `Clients` | empty | Other applications that sign in here. Each has a `ClientId`, an optional `ClientSecret` and `DisplayName`, and `RedirectUris`. |
| `Groups` | empty | Regular expressions that select the directory groups sent as roles. Empty sends no roles. |
| `PersistKeys` | `true` | Keeps the signing and encryption keys across restarts. |
| `DataPath` | `App_Data/keys/identity-server` | The key folder, absolute or relative to the content root. |
| `AccessTokenLifetime` | unset | The lifetime of access tokens, such as `01:00:00`. Unset keeps the server's default. |
| `IdentityTokenLifetime` | unset | The lifetime of identity tokens. Unset keeps the server's default. |
| `AuthorizationCodeLifetime` | unset | The lifetime of authorization codes. Unset keeps the server's default. |

### BackOfficeLogin

| Setting | Default | What it does |
|---|---|---|
| `DefaultFlow` | `Auto` | The flow of the login screen: `Auto`, `Form`, `IdentityServer` or `Debug`, as **Backoffice login** below describes. |
| `DebugFlow` | `Debug` | The flow while `Umbraco:CMS:Hosting:Debug` is true. `null` uses `DefaultFlow`. |

### Accounts

| Setting | Default | What it does |
|---|---|---|
| `EnsureMemberGroups` | empty | Member groups to create at startup when they are missing. |
| `EnsureUserGroups` | empty | User groups to create at startup when they are missing. |
| `EnsureBackOfficeUsers` | empty | Backoffice users to create at startup when they are missing. Each has an `Email`, a `Name` and `UserGroups`, a list of group aliases. |
| `EnsureMembers` | empty | Members to create at startup when they are missing. Each has an `Email`, a `Name` and `MemberGroups`, a list of group names. |
| `MemberTypeAlias` | `Member` | The member type of members created from `EnsureMembers` or by `AccountProvisioning`. |
| `RetireDeletedUsers` | `true` | Retires a deleted user who has signed in or is linked, in place of deleting it. |
| `RetireDeletedMembers` | `true` | The same for members. |
| `SeedCurrentWindowsUserAsAdministrator` | `false` | For development: creates `<Windows user>@localhost` as an administrator at startup. |
| `BackOfficeUsersManagedBy` | `Umbraco` | The policy of `AccountProvisioning` for users. `Umbraco` links to accounts an administrator created. `ActiveDirectory` also creates accounts from directory groups. |
| `MembersManagedBy` | `Umbraco` | The same for members. |
| `ActiveDirectoryGroupPattern` | unset | A regular expression that selects the Umbraco groups `ActiveDirectory` mode manages. Unset manages every group, Administrators included. In that mode, an invalid pattern stops startup. |
| `LinkByUserNameOnFirstSignIn` | `true` | Lets `AccountProvisioning` match an unlinked account by email or username. |

## Sign-in flows

The package registers one OpenID Connect handler for each audience: the schemes
`Umbraco.<Name>` and `UmbracoMembers.<Name>`, with the callbacks `/signin-oidc/backoffice`
and `/signin-oidc/member`. The handler uses the authorization code flow with a `GET`
callback and `SameSite=Lax` cookies, so the identity server can be on any host and the site
can run over plain HTTP. Over HTTPS, the cookies are marked `Secure`.

The handler validates the protocol and passes the identity to Umbraco, which finds the
stored link, creates or links an account when the auto-link settings allow, and runs its
two-factor checks. Umbraco stores the `sub` claim as the key of the link, which is the
Windows SID from the embedded server, and auto-linking reads the `email` and `name` claims.

### Backoffice login

| Flow | Behavior |
|---|---|
| `Auto` | Redirects to the identity server when the backoffice provider is enabled and can sign people in where the site runs. Shows the password form otherwise. |
| `Form` | Shows the password form, with the provider button beside it. |
| `IdentityServer` | Redirects to the identity server. When the backoffice provider is off, startup stops with a configuration error. |
| `Debug` | Shows the password form and never redirects. Outside debug mode it acts as `Form`. |

The redirect applies to the first sign-in. After a sign-out or a session timeout, Umbraco
shows its choice of providers.

### Fallback to the password form

A backoffice sign-in that fails returns the browser to
`/umbraco/login?ReturnUrl=...&sso-denied=<reason>`, where Umbraco shows its password form.
Three failures lead there:

1. The handler cannot read the server's metadata before it redirects.
2. The server returns an error, or the browser lost the correlation cookie.
3. Umbraco finds no account linked to the identity. Umbraco opens the password form itself,
   and the package removes `identity_provider` from its return address.

The return address keeps the `state` and the PKCE `code_challenge` of the original
authorization request, so a valid password completes that request. When the return address
is missing, names another host or endpoint, or has a fragment, the browser goes to
`/umbraco/logout`, which shows the choice of providers without a redirect.

### Member sign-in

`MemberLoginController` starts a member provider from any page and returns to that page
afterwards:

```cshtml
@using IdentityServer.Sso
<a href="@SsoLoginUrls.SurfaceAction(Url, nameof(MemberLoginController.Login), "MemberLogin",
    new { provider = "UmbracoMembers.Domain", returnUrl = Context.Request.Path + Context.Request.QueryString })">Sign in</a>
```

`Login` answers a link. `Retry` answers a posted form, with Umbraco's anti-forgery and form
route checks. Both use Umbraco's own callback, so two-factor sign-in works as it does for a
password.

When the provider is not offered or the sign-in fails, the browser returns to the page with
`sso-local=1` and `sso-denied=<reason>` added. Make the site's sign-in view show its
password form, the reason and a retry button when it sees `sso-local`.
`SsoLoginUrls.CleanReturnUrl` accepts local addresses only and removes both parameters.

`SsoProviderCatalog` lists the enabled providers. Its `BackOfficeLogins` and `MemberLogins`
hold the providers that can sign someone in where the site runs: the embedded server needs
Windows authentication, and an external server authenticates people itself.

## The embedded identity server

The embedded server answers discovery at `/.well-known/openid-configuration`, keys at
`/.well-known/jwks`, Windows sign-in at `/connect/authorize` and code redemption at
`/connect/token`. Its issuer is the address of the request, so applications use the address
the site is published under. It has its own keys and its own list of clients, separate from
Umbraco's authentication server.

The site's own client signs in without registration. Other applications are listed in
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

Every client uses the authorization code flow with PKCE and an exact redirect address. The
server issues no refresh tokens, so a client starts a new sign-in when it needs fresh
tokens. The
[embedded server guide](https://github.com/auroris/AurorisNet.Umbraco.IdentityServer.SSO/blob/main/docs/EMBEDDED-SERVER.md)
has the client rules, the claims for each scope, key storage, IIS setup and troubleshooting.

## Who gets an account

**At sign-in.** Umbraco creates and links accounts as the `BackOfficeAutoLink` and
`MemberAutoLink` settings say. For the backoffice, auto-linking starts off: an administrator
creates the user and its link, or the user links the provider from their profile after a
password sign-in. For members, auto-linking starts on.

**Configured accounts.** Once the site runs, `ConfiguredAccounts` creates what the
`Accounts` section lists, in this order, and leaves existing groups and accounts as they
are:

1. The member groups in `EnsureMemberGroups`.
2. The members in `EnsureMembers`, approved, with the email as username.
3. The user groups in `EnsureUserGroups`, with an alias made from the name and access to the
   Content section.
4. The backoffice users in `EnsureBackOfficeUsers`, with the email as username, and the
   current Windows account when `SeedCurrentWindowsUserAsAdministrator` is true.

The site logs a step that fails and runs the next one. The accounts get a random password,
and an administrator sets a usable one in **Users** or **Members**. A host that imports
content naming these groups before the site has started calls
`ConfiguredAccounts.EnsureGroupsAsync()` first.

**Retirement.** Deleting a user or member who has signed in, or who is linked to an external
login, retires the account:

- Its username becomes the SID it was linked to, or `retired-` and the account key. Its
  email becomes that username at `retired.invalid`, a reserved domain that delivers nothing.
- It is disabled and gets a new security stamp.
- Its name, groups, history and external links stay, and an audit entry records the
  username and email it had.

The retired account still holds the person's link, so their next sign-in finds a disabled
account, and a new person can take the freed username and email. To restore the person,
enable the account and give back its username and email. An account that never signed in
and has no link is deleted as Umbraco deletes it. The built-in administrator is never
retired. With `RetireDeletedUsers` or `RetireDeletedMembers` set to `false`, deleting works
as it does in Umbraco.

**Accounts from directory facts.** `AccountProvisioning` creates and links accounts for
code that holds a person's directory facts as an `SsoAccount`. The OpenID Connect sign-in
leaves it unused.

- An account belongs to the first Windows SID linked to it. The SID is the provider key
  under every scheme, so a second provider adds a link to the same account.
- With `LinkByUserNameOnFirstSignIn`, an unlinked account is matched by email or username,
  ignoring case. An account linked to another SID is refused, because a directory can give
  a new person the username of someone it deleted.
- With `BackOfficeUsersManagedBy` or `MembersManagedBy` set to `ActiveDirectory`, a first
  sign-in creates an account when a directory group has a managed Umbraco group of the same
  name, and the managed memberships follow the directory at every sign-in.
- `LinkAsync` links an existing account to a SID.

## Supported versions

| Umbraco | Supported releases | Runtime |
|---|---|---|
| 18 | <!-- umbraco-releases 18 -->18.0.0 to 18.3.2<!-- /umbraco-releases --> | .NET 10 |

The package accepts every release in these ranges, and the tests run on the first and the
newest release of each major version.

## Documentation

| Page | Covers |
|---|---|
| [Embedded identity server and IIS](https://github.com/auroris/AurorisNet.Umbraco.IdentityServer.SSO/blob/main/docs/EMBEDDED-SERVER.md) | Registering clients, the claims and keys of the embedded server, IIS setup with the helper script, and troubleshooting. |
| [Security policy](https://github.com/auroris/AurorisNet.Umbraco.IdentityServer.SSO/blob/main/SECURITY.md) | Reporting a vulnerability, and the trust the site's configuration decides. |

## Developing

Building needs the .NET 10 SDK, which `global.json` selects, and a checkout of
[AurorisNet.IdentityServer](https://github.com/auroris/AurorisNet.IdentityServer) beside this
repository, at `../AurorisNet.IdentityServer`. The Windows sign-in test needs Windows.

```powershell
dotnet build -c Release
# The tests on the newest supported Umbraco release
dotnet test --project tests/IdentityServer.Sso.Tests -c Release
# The tests on another release
dotnet test --project tests/IdentityServer.Sso.Tests -c Release -p:UmbracoVersion=18.0.0
# A package, numbered 0.0.0-local
dotnet pack src/AurorisNet.Umbraco.IdentityServer.SSO.csproj -c Release -o artifacts/packages
```

The full test matrix is the test command run once for each release in `UmbracoReleases` in
`Directory.Build.props`. It runs on a developer's machine. The workflows test one release.

Two properties of `src/AurorisNet.Umbraco.IdentityServer.SSO.csproj` choose where
`AurorisNet.IdentityServer` comes from:

| Property | Default | What it does |
|---|---|---|
| `UseIdentityServerSource` | `true` | `true` builds `IdentityServer.Core/IdentityServer.Core.csproj` from the checkout. `false` references the published package, at the version in `Directory.Packages.props`. |
| `IdentityServerRoot` | `../AurorisNet.IdentityServer` | The checkout to build from, such as `-p:IdentityServerRoot=D:\work\AurorisNet.IdentityServer`. |

The workflows build with `UseIdentityServerSource` set to `false`, and a release requires
it: a pack with `ContinuousIntegrationBuild` stops with an error otherwise. To build that
way locally, add `-p:UseIdentityServerSource=false`. The workflows also turn build warnings
into errors, so keep the build free of warnings.

The tests start Umbraco sites with SQLite and Kestrel, one at a time. The Windows sign-in
test signs in the account that runs the tests.

The package calls `AddBackOfficeExternalLogins`, `AddMemberExternalLogins`, the auto-link
option classes, `BackOfficeLoginController`, `IPackageManifestReader`, `IUserService`,
`IMemberEditingService`, the events of Umbraco's OpenIddict server and
`UmbracoPipelineFilter`. Look there first when a new Umbraco release fails the tests. The
embedded host also replaces the client validation handlers of `AurorisNet.IdentityServer`,
and stops at startup when an update of that package changes their number.

## Releases

The package follows Umbraco and `AurorisNet.IdentityServer`. Each week,
`umbraco-releases.yml` looks on nuget.org for new Umbraco releases and tests the package on
each one. A release that passes is committed to `UmbracoReleases` in
`Directory.Build.props`, which also rewrites the ranges under **Supported versions**, and
`weekly-release.yml` then publishes a new version. A release that fails gets an issue.

Dependabot follows `AurorisNet.IdentityServer`. An update that passes `ci.yml` is merged.
One weekly release publishes the updates and the Umbraco releases of the week.

Versions are numbered MAJOR.MINOR.PATCH. A new Umbraco release raises the patch number, or
the minor number when it is a new Umbraco minor version. A person starts the first release
by hand, with a version, once `AurorisNet.IdentityServer` is on nuget.org at the version in
`Directory.Packages.props`. The
[release guide](https://github.com/auroris/AurorisNet.Build/blob/main/docs/releasing.md)
describes the workflows.

## License

[MIT](https://github.com/auroris/AurorisNet.Umbraco.IdentityServer.SSO/blob/main/LICENSE).
