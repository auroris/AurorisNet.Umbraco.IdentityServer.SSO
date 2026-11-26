# Embedded identity server and IIS

This page covers the identity server the site hosts in its own process: how to register
clients, which claims and keys it issues, how to set it up under IIS, and how to find the
cause of a failed sign-in. The [README](../README.md) has the installation steps, the
settings tables and the sign-in flows.

The site signs in through the embedded server when `IdentityServer:Authority` is empty. The
server is `AurorisNet.IdentityServer`, which authenticates Windows accounts with Kerberos or
NTLM and issues OpenID Connect tokens to this site and to the applications listed as its
clients.

With `EmbeddedIdentityServer:Enabled` left out, the server runs in two cases: this site
signs in through it, with the provider enabled for the backoffice or for members and
`Authority` empty, or `Clients` lists an application. Set `Enabled` to `false` with an
external `Authority` to leave the server out.

## Endpoints and issuer

The server has its own services, authentication schemes, keys and OpenIddict options. It
receives these requests only:

| Endpoint | Purpose |
|---|---|
| `/.well-known/openid-configuration` | Discovery |
| `/.well-known/jwks` | Signing keys |
| `/connect/authorize` | Windows sign-in, with the authorization code flow and PKCE (`S256`) |
| `/connect/token` | Code redemption |

The issuer is the address of the request: its scheme, host and virtual path. Applications
use the address the site is published under. Behind a reverse proxy, configure forwarded
headers so the site sees its public address, and restrict `AllowedHosts` to the site's real
host names, which limits the addresses the issuer can take.

## Register a client

The site's own client (`IdentityServer:ClientId`) signs in without registration, at its
enabled callbacks only, on the exact scheme, host, port and virtual path of the request.
List other applications in `EmbeddedIdentityServer:Clients`, then restart the site:

```json
"EmbeddedIdentityServer": {
  "Clients": [
    {
      "ClientId": "reports-portal",
      "ClientSecret": "a long random secret",
      "DisplayName": "Reports portal",
      "RedirectUris": [ "https://reports.example/signin-oidc" ]
    }
  ],
  "Groups": [ "^Report ", "^Domain Admins$" ]
}
```

| Field | What it does |
|---|---|
| `ClientId` | The `client_id` the application sends. Required. |
| `ClientSecret` | Makes the client confidential: it presents the secret at the token endpoint. Empty registers a public client. |
| `DisplayName` | A label for the client. |
| `RedirectUris` | The exact callback addresses the client may receive codes on. At least one. |
| `PostLogoutRedirectUris` | Accepted, and checked as addresses. The server has no sign-out endpoint, so it never uses them. |

Every client must use PKCE, and a redirect address must match a listed one exactly: the
server accepts no wildcards and no match on the host alone. The server issues no refresh
tokens and has no userinfo or sign-out endpoint, so a client starts a new sign-in when it
needs fresh tokens.

While the embedded server is enabled, startup stops with a message that names the problem
when:

- a client id is missing, appears twice, belongs to Umbraco (`umbraco-back-office`,
  `umbraco-member`, `umbraco-swagger`, `umbraco-postman`) or is this site's own client id;
- this site signs in through the embedded server and `IdentityServer:ClientId` belongs to
  Umbraco;
- a client has no redirect address, or has an address that is not an absolute `http` or
  `https` address without a fragment;
- a `Groups` pattern does not compile, or a token lifetime is zero or negative;
- `EmbeddedIdentityServer:KeyPath` or `EmbeddedIdentityServer:UseActiveDirectory` is
  present. Name the key folder in `DataPath`, and remove `UseActiveDirectory`: the server
  reads Active Directory for every domain account.

## Claims

| Scope | Claims |
|---|---|
| `openid` | `sub`, the Windows SID, and `.../identity/claims/name`, the directory display name or the account name |
| `profile` | `.../windowsaccountname` (`DOMAIN\user`), and `.../givenname`, `.../surname` and `.../homephone` when the directory has them |
| `email` | `.../emailaddress`, the directory email or `user@localhost` |
| `roles` | one `.../claims/role` for each direct group membership that `Groups` selects, without the domain prefix |

`Groups` holds .NET regular expressions, matched against direct group names without regard
to case. The server expands no nested groups, and local machine accounts carry no roles.
Narrow `Groups` to keep tokens small.

## Keys

The server stores 2048-bit RSA signing and encryption keys as `signing-key.bin` and
`encryption-key.bin` in `DataPath`, protected with Windows machine DPAPI. Persisted keys
let the server validate issued tokens after a restart. The embedded issuer keeps outstanding
codes in process memory. Each code can be exchanged once in the process that issued it.
Run the site as one instance, with one worker process in its application pool, so the token
request reaches that process. If the site recycles during sign-in, start a fresh sign-in to
get a new code.

- Keep the folder outside the published application files, so an update keeps the keys. The
  default, `App_Data/keys/identity-server`, qualifies: a publish writes no files to
  `App_Data`.
- Restrict the folder to the service account and administrators.
- The keys are readable only on the machine that wrote them. A copied folder stops startup
  on another machine, as any unreadable key file does.
- `PersistKeys` set to `false` creates new keys at every start, which invalidates issued
  tokens and outstanding codes.

## Separation from Umbraco's server

The embedded server and Umbraco's own authentication server have separate clients and
separate keys. The embedded server refuses Umbraco's clients at `/connect/`, and its own
clients exist in its configuration only. The package leaves Umbraco's grant types, PKCE
requirement, token formats and lifetimes as Umbraco sets them.

When the site signs in through its own server, it redeems the code by calling its own
`/connect/token` at the address the browser used. DNS, IIS bindings, firewalls and proxies
must allow that call. For a certificate the server does not trust, set
`IdentityServer:AcceptAnyServerCertificate`. The site takes the issuer and the signing keys
from the server in the same process, so a forged `Host` header cannot point the sign-in at
another server.

## IIS Windows authentication

The embedded server authenticates people itself, so its site needs both **Windows
Authentication** and **Anonymous Authentication** in IIS. The token and discovery endpoints
answer without a challenge, and the authorize endpoint challenges a request that carries no
Windows credentials yet. On Kestrel, the Negotiate handler performs the exchange itself.

1. Enable **Anonymous Authentication** and **Windows Authentication** on the IIS
   application.
2. From an elevated PowerShell on the server, run the helper with the site and the
   application path (`/` for a root application). The script is in the NuGet package's
   `tools` folder and in this repository's `tools` folder:

   ```powershell
   .\tools\configure-iis-authentication.ps1 -Site 'Default Web Site' -ApplicationPath '/intranet'
   ```

   It prints:

   ```text
   Configured Default Web Site/intranet/umbraco. Windows SSO remains enabled outside the backoffice path.
   ```

The helper switches Windows Authentication off under the application's `umbraco` path, where
Umbraco owns authentication. Without it, an unauthenticated backoffice API response after a
sign-out also carries `Negotiate` headers, and the browser opens a Windows Security dialog.

The helper also copies the application's ASP.NET Core handler to that path, so requests
still reach Umbraco. It refuses to run when the application has no ASP.NET Core Module V2
handler or when either authentication method is off. It can run again safely, and `-WhatIf`
shows the change without making it. The helper writes to IIS's own configuration
(`applicationHost.config`), so an application update keeps the change.

### With Anonymous Authentication only

The site starts and logs a warning that Windows authentication is unavailable, which names
how people sign in instead. The login screens leave out the embedded server, nothing
redirects to it, and the embedded server answers every authorization request with
`access_denied`. People sign in with a password or through an external server. To restore
Windows sign-in, enable Windows Authentication for the site and restart it.

## Troubleshooting

| Symptom | Cause and remedy |
|---|---|
| The browser keeps prompting for credentials, or `/connect/authorize` answers 401 repeatedly. | Browsers send Windows credentials silently only to sites in the Local Intranet zone (Edge, Chrome) or listed in `network.negotiate-auth.trusted-uris` (Firefox). Add the site there. In IIS, check that Windows Authentication is enabled. |
| A domain account signs in with NTLM where Kerberos is expected. | The host name has no SPN for the application pool's account. Register one with `setspn -S HTTP/host.domain DOMAIN\account`. NTLM still signs people in, and local accounts always use it. |
| `/connect/authorize` answers `invalid_request` and names the client id or the redirect address. | The client is not listed, or its redirect address differs from the listed one. |
| The browser returns to the password form after `/signin-oidc/...`. | The site could not redeem the code: it could not reach its own token endpoint at the address the browser used, or it restarted after it issued the code. The log records the failure, and names a code the server refused as `Authorization code refused`. See [Separation from Umbraco's server](#separation-from-umbracos-server). |
| The embedded server answers `access_denied`, and the login screen has no provider button. | IIS has Anonymous Authentication only. See [With Anonymous Authentication only](#with-anonymous-authentication-only). |
| Startup stops with `IdentityServer:Authority is empty ... EmbeddedIdentityServer:Enabled is false`. | Enable the embedded server, or set `Authority` to an external server. |
| A Windows Security dialog opens after a backoffice sign-out. | IIS adds Windows challenges under `umbraco`. Run the helper script from [IIS Windows authentication](#iis-windows-authentication). |
