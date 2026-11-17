# Security

## Report a vulnerability

Report a vulnerability in private, with
[GitHub's vulnerability report form](https://github.com/auroris/AurorisNet.Umbraco.IdentityServer.SSO/security/advisories/new).
You also reach it from this repository's **Security** tab, under **Report a vulnerability**.
Please keep the details out of public issues until a fix is published.

A report is easier to act on when it includes:

- the package version and the site's Umbraco release;
- the settings involved, without real secrets;
- whether the site signs in through its embedded identity server or an external one;
- the steps that show the problem, and what someone gains by it.

## Versions that get fixes

A fix is published as a new package version. Earlier versions stay as they are, so install
the newest version for your site; a `1.*` reference picks it up on the next restore.

## What the package covers

The site's configuration decides whom the site trusts, so some access is by design:

- Anyone who can write `appsettings.json`, or set the matching environment variables, chooses
  the identity server the site trusts and the applications that may sign in through its
  embedded server. Protect the configuration as you would the accounts themselves.
- `IdentityServer:AcceptAnyServerCertificate` turns off certificate validation for calls to the
  identity server.

A vulnerability is anything beyond that, for example: a sign-in to an account linked to another
identity, a retired account signing in, the embedded server issuing a code or token to a client
or redirect address it does not list, a protocol failure that bypasses Umbraco's password or
two-factor checks, or a return address that leaves the site. For a vulnerability in the OpenID
Connect server library, report it to `AurorisNet.IdentityServer`; for one in Umbraco itself,
follow [Umbraco's security policy](https://github.com/umbraco/Umbraco-CMS/security/policy).
