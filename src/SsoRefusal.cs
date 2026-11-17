using Microsoft.AspNetCore.Authentication;
using Umbraco.Cms.Core;

namespace IdentityServer.Sso;

/// <summary>
/// Returns unsuccessful SSO attempts to local password sign-in without automatically retrying the provider.
/// </summary>
public static class SsoRefusal
{
    /// <summary>Query parameter the member flow adds to the return URL when sign-in is refused.</summary>
    public const string DeniedParameter = "sso-denied";

    /// <summary>Whether <paramref name="scheme"/> is a backoffice scheme rather than a member scheme.</summary>
    public static bool IsBackOfficeScheme(string scheme)
        => scheme.StartsWith(Constants.Security.BackOfficeExternalAuthenticationTypePrefix, StringComparison.Ordinal);

    /// <summary>Writes the refusal response for the scheme's audience.</summary>
    public static Task WriteAsync(HttpContext context, string scheme, string providerName, AuthenticationProperties? properties, string reason, string status)
        => IsBackOfficeScheme(scheme)
            ? BackOfficeSsoFallback.RedirectAsync(context, properties, reason)
            : RedirectMemberAsync(context, properties, reason);

    private static Task RedirectMemberAsync(HttpContext context, AuthenticationProperties? properties, string reason)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Redirect(SsoLoginUrls.LocalMemberUrl(context, SsoLoginUrls.MemberReturnUrl(context, properties), reason));
        return Task.CompletedTask;
    }
}
