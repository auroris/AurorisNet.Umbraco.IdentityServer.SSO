# Configuration, sign-in flows, and account rules

Changes apply on restart except `Accounts:RetireDeletedUsers` and `Accounts:RetireDeletedMembers`, which apply on the next delete.

## Settings

### `IdentityServer`

| Setting | Default | Effect |
|---|---|---|
| `Enabled` | `false` | Registers the provider (`ClientId` required). |
| `Name` | `Identity Server` | Button label; letters/digits form scheme names `Umbraco.<Name>` and `UmbracoMembers.<Name>`. **Keep stable once accounts are linked.** |
| `Authority` | empty | External issuer URL (empty uses the embedded server). |
| `ClientId` / `ClientSecret` | empty | OIDC client ID and optional secret. |
| `Scopes` | empty | Requested scopes (empty uses `openid profile email roles`). |
| `CallbackPath` | `/signin-oidc` | Prefix followed by `/backoffice` or `/member` (reserved from content routing). |
| `BackOfficeEnabled` / `MemberEnabled` | `true` | Enable SSO per audience. |
| `DenyLocalLogin` | `false` | Hides the backoffice password form. |
| `RequireHttpsMetadata` | `true` | Rejects `http://` external authorities. |
| `AcceptAnyServerCertificate` | `false` | Skips TLS checks for discovery/keys/tokens (**isolated intranets only**). |
| `GetClaimsFromUserInfoEndpoint` | `false` | Also fetch claims from the userinfo endpoint. |
| `DirectoryApiKey` | empty | Bound for host use; unused by this package. |
| `BackOfficeAutoLink:*` | `AutoLinkExternalAccount=false`, `AllowManualLinking=true`, `DefaultUserGroups=[]`, `DefaultCulture=unset` | Controls backoffice auto-linking and profile linking. |
| `MemberAutoLink:*` | `AutoLinkExternalAccount=true`, `DefaultIsApproved=true`, `DefaultMemberTypeAlias=Member`, `DefaultCulture=unset`, `DefaultMemberGroups=[]`, `ExternalOnly=false` | Controls member auto-linking. |

### `EmbeddedIdentityServer`

| Setting | Default | Effect |
|---|---|---|
| `Enabled` | unset | Unset runs automatically when `Authority` is empty or `Clients` is non-empty. |
| `Clients` | empty | Additional OIDC apps (`ClientId`, optional `ClientSecret`/`DisplayName`, `RedirectUris`). |
| `Groups` | empty | Regexes selecting directory groups sent as `role` claims. |
| `PersistKeys` | `true` | Keep signing/encryption keys across restarts. |
| `DataPath` | `App_Data/keys/identity-server` | Key folder (absolute or relative to content root). |
| `AccessTokenLifetime`, `IdentityTokenLifetime`, `AuthorizationCodeLifetime` | unset | Optional `hh:mm:ss` overrides. |

### `BackOfficeLogin`

| Setting | Default | Effect |
|---|---|---|
| `DefaultFlow` | `Auto` | `Auto` (redirect when backoffice provider can sign in, else form), `Form` (password + button), `IdentityServer` (always redirect; fails startup if provider is off), or `Debug` (never redirects). |
| `DebugFlow` | `Debug` | Flow used while `Umbraco:CMS:Hosting:Debug` is `true` (`null` uses `DefaultFlow`). |

### `Accounts`

| Setting | Default | Effect |
|---|---|---|
| `EnsureMemberGroups` / `EnsureUserGroups` | empty | Groups created at startup if missing. |
| `EnsureMembers` / `EnsureBackOfficeUsers` | empty | Accounts (`Email`, `Name`, `MemberGroups`/`UserGroups`) created at startup with random passwords. |
| `MemberTypeAlias` | `Member` | Member type for seeded/provisioned members. |
| `RetireDeletedUsers` / `RetireDeletedMembers` | `true` | Soft-retire deleted accounts that signed in or have external links. |
| `SeedCurrentWindowsUserAsAdministrator` | `false` | Dev helper: creates `<Windows user>@localhost` as an administrator at startup. |
| `BackOfficeUsersManagedBy` / `MembersManagedBy` | `Umbraco` | `AccountProvisioning` mode: `Umbraco` (link existing) or `ActiveDirectory` (create/sync from directory groups). |
| `ActiveDirectoryGroupPattern` | unset | Regex selecting Umbraco groups managed in `ActiveDirectory` mode (unset manages all groups). |
| `LinkByUserNameOnFirstSignIn` | `true` | Allows `AccountProvisioning` to claim unlinked accounts by email/username. |

## Sign-in flows

- Uses OIDC authorization code flow with `GET` callbacks and `SameSite=Lax` cookies (`Secure` over HTTPS). Umbraco stores `sub` (Windows SID for the embedded server) as the external login key.
- Failed backoffice sign-ins redirect to `/umbraco/login?ReturnUrl=...&sso-denied=<reason>` preserving PKCE/state so password login can finish the request (invalid return URLs go to `/umbraco/logout`).
- Member sign-in helper:

```cshtml
@using IdentityServer.Sso
<a href="@SsoLoginUrls.SurfaceAction(Url, nameof(MemberLoginController.Login), "MemberLogin",
    new { provider = "UmbracoMembers.Domain", returnUrl = Context.Request.Path + Context.Request.QueryString })">Sign in</a>
```

On failure, members return with `sso-local=1&sso-denied=<reason>`. Use `SsoLoginUrls.CleanReturnUrl` and `SsoProviderCatalog` (`BackOfficeLogins` / `MemberLogins`) in custom login views.

## Embedded server endpoints

Serves `/.well-known/openid-configuration`, `/.well-known/jwks`, `/connect/authorize`, and `/connect/token` using PKCE and exact redirect URIs (no refresh tokens). See [EMBEDDED-SERVER.md](EMBEDDED-SERVER.md).

## Account retirement and provisioning

- **Retirement:** Deleting a linked or previously signed-in user/member (other than the built-in admin) disables it, rotates its security stamp, renames username to its SID (or `retired-<key>`) and email to `<username>@retired.invalid`, preserving history and audit entries while freeing the original username/email.
- **Startup seeding:** `ConfiguredAccounts` creates listed groups/accounts in order (call `ConfiguredAccounts.EnsureGroupsAsync()` early if importing content before startup).
- **`AccountProvisioning`:** Helper for code holding directory `SsoAccount` data (unused by the OIDC handler itself); links by SID via `LinkAsync` or optional AD group sync.
