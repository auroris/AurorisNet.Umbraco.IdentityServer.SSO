# Embedded identity server and IIS

With `IdentityServer:Authority` empty, the site signs people in through an identity server in its
own process: IdentityServer.Core from `AurorisNet.IdentityServer`, which authenticates Windows
accounts with Kerberos or NTLM and issues OpenID Connect tokens to this site and to the
applications listed as its clients. This page covers registering clients, the claims and keys
the server issues, deployment under IIS with the helper script, and troubleshooting. The
[README](../README.md) covers installation, the settings tables and the sign-in flows.

`EmbeddedIdentityServer:Enabled` left out runs the server when this site signs in through it (the
provider is enabled for at least one audience and `Authority` is empty) or when `Clients` lists an
application. Set it to `false` with an external `Authority` to leave the server out.

## Endpoints and issuer

The server runs with its own service container, authentication schemes, keys and OpenIddict
options. Only these requests reach it:

| Endpoint | Purpose |
|---|---|
| `/.well-known/openid-configuration` | Discovery |
| `/.well-known/jwks` | Signing keys |
| `/connect/authorize` | Windows sign-in; the authorization code flow with PKCE (`S256`) |
| `/connect/token` | Code redemption |

The issuer is the address of the request: scheme, host and virtual path, as Umbraco's own
OpenIddict endpoints use. Applications use the address the site is published under. Behind a
reverse proxy, configure forwarded headers so the site sees its public address, and restrict
`AllowedHosts` to the site's real host names to limit the addresses the issuer can take.

## Register a client

The site's own client (`IdentityServer:ClientId`) signs in without registration, and only at its
enabled callback on the exact scheme, host, port and virtual path of the request. Other
applications are listed in `EmbeddedIdentityServer:Clients` and take effect at the next restart:

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

| Field | Effect |
|---|---|
| `ClientId` | The `client_id` the application sends. Required. |
| `ClientSecret` | Makes the client confidential: it presents the secret at the token endpoint. Empty registers a public client. |
| `DisplayName` | A label for the client. |
| `RedirectUris` | The exact callback addresses the client may receive codes on. At least one. |
| `PostLogoutRedirectUris` | Accepted and checked for configuration compatibility; the server offers no sign-out endpoint. |

Every client must use PKCE, and redirect addresses must match exactly, with no wildcards or
host-only matching. The server issues no refresh tokens and has no userinfo or sign-out endpoint,
so a client starts a new sign-in when it needs fresh tokens.

While the embedded server is enabled, startup stops with a message naming the problem when:

- a client id is missing, appears twice, belongs to Umbraco (`umbraco-back-office`,
  `umbraco-member`, `umbraco-swagger`, `umbraco-postman`) or is this site's own client id;
- this site signs in through the embedded server and `IdentityServer:ClientId` belongs to Umbraco;
- a client has no redirect address, or an address that is not an absolute `http` or `https`
  address without a fragment;
- a `Groups` pattern does not compile, or a token lifetime is zero or negative;
- `EmbeddedIdentityServer:KeyPath` or `UseActiveDirectory`, settings of earlier releases, are
  present. Move the key folder setting to `DataPath`, and remove `UseActiveDirectory`: the server
  reads Active Directory for every domain account.

## Claims

| Scope | Claims |
|---|---|
| `openid` | `sub`, the Windows SID, and `.../identity/claims/name`, the directory display name or the account name |
| `profile` | `.../windowsaccountname` (`DOMAIN\user`), and `.../givenname`, `.../surname` and `.../homephone` when the directory has them |
| `email` | `.../emailaddress`, the directory email or `user@localhost` |
| `roles` | one `.../claims/role` per direct group membership that `Groups` selects, without the domain prefix |

`Groups` holds case-insensitive .NET regular expressions matched against direct group names; the
server expands no nested groups. Local machine accounts carry no roles. Narrow `Groups` to keep
tokens small.

## Keys

IdentityServer.Core stores 2048-bit RSA signing and encryption keys as `signing-key.bin` and
`encryption-key.bin` in `DataPath`, protected with Windows machine DPAPI. Persisted keys let the
server redeem an unexpired authorization code and validate issued tokens after a restart.

- Keep the folder outside the published application files so updates keep it. The default,
  `App_Data/keys/identity-server`, qualifies: a publish writes no files to `App_Data`.
- Restrict the folder to the service account and administrators.
- Keys stay readable only on the machine that wrote them. A copied folder stops startup on
  another machine, as any unreadable key file does.
- `PersistKeys` set to `false` creates new keys at every start, which invalidates issued tokens
  and outstanding codes.

## Separation from Umbraco's server

Umbraco's clients are refused at `/connect/`, the embedded server's clients are refused at
Umbraco's own authorize and token endpoints, and Umbraco's APIs refuse access tokens issued here.
Umbraco's grant types, PKCE requirement, token formats and lifetimes stay as Umbraco sets them.

When the site signs in through its own server, it redeems the code by calling its own
`/connect/token` at the address the browser used. DNS, IIS bindings, firewalls and proxies must
allow that call, and `IdentityServer:AcceptAnyServerCertificate` covers a certificate the server
does not trust. The issuer and signing keys come from the server in the same process, so a forged
`Host` header cannot point the sign-in at another server.

## IIS Windows authentication

The embedded server authenticates people itself, so its site needs both **Windows
Authentication** and **Anonymous Authentication** in IIS. The token and discovery endpoints answer
without a challenge, and the authorize endpoint challenges when nothing has authenticated yet. On
Kestrel the Negotiate handler performs the exchange itself.

1. Enable **Anonymous Authentication** and **Windows Authentication** on the IIS application.
2. From an elevated PowerShell on the server, run the helper with the site and application path
   (`/` for a root application). The script is in the NuGet package's `tools` folder and in this
   repository's `tools` folder:

   ```powershell
   .\tools\configure-iis-authentication.ps1 -Site 'Default Web Site' -ApplicationPath '/intranet'
   ```

   It prints:

   ```text
   Configured Default Web Site/intranet/umbraco. Windows SSO remains enabled outside the backoffice path.
   ```

The helper switches IIS Windows challenges off under `umbraco`, where Umbraco owns
authentication. Without it, an unauthenticated backoffice API response after logout also carries
`Negotiate` headers, and the browser opens a Windows Security dialog. The helper copies the
application's ASP.NET Core handler to that path so requests still reach Umbraco, and refuses to
run when the application lacks an ASP.NET Core Module V2 handler or either authentication method
is off. It can run again safely and supports `-WhatIf`. The settings live in IIS configuration
(`applicationHost.config`) and survive application updates.

### With Anonymous Authentication only

The site starts and logs a warning that Windows authentication is unavailable, naming how people
sign in instead. The login screens leave out the embedded server, nothing redirects
automatically, and the embedded server answers every authorization request with
`access_denied`. People sign in with a password or through an external server. To restore Windows
sign-in, enable Windows Authentication for the site and restart it.

## Troubleshooting

| Symptom | Cause and remedy |
|---|---|
| The browser keeps prompting for credentials, or `/connect/authorize` answers 401 repeatedly. | Browsers send Windows credentials silently only to sites in the Local Intranet zone (Edge, Chrome) or listed in `network.negotiate-auth.trusted-uris` (Firefox). Add the site there. In IIS, check that Windows Authentication is enabled. |
| `/connect/authorize` fails under IIS with an error naming `IConnectionItemsFeature`. | Windows Authentication is off for the site, so the Negotiate handler attempts the exchange itself, which only Kestrel supports. Enable it. |
| A domain account signs in with NTLM instead of Kerberos. | The host name has no SPN for the application pool's account. Register one with `setspn -S HTTP/host.domain DOMAIN\account`. NTLM still works, and local accounts always use it. |
| `invalid_request` naming the client id or the redirect address. | The client is unregistered, or its redirect address differs from the registered one. The log names the check that failed. |
| Sign-in ends in an error after `/signin-oidc/...`. | The site could not reach its own token endpoint at the address the browser used. The log records the failure; see [Separation from Umbraco's server](#separation-from-umbracos-server). |
| Startup stops with `IdentityServer:Authority is empty ... EmbeddedIdentityServer:Enabled is false`. | Enable the embedded server, or set `Authority` to an external server. |
| A Windows Security dialog opens after backoffice logout. | IIS adds Windows challenges under `umbraco`. Run the helper script from [IIS Windows authentication](#iis-windows-authentication). |
