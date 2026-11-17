using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using Umbraco.Cms.Api.Management;

namespace IdentityServer.Sso;

/// <summary>Makes the native password form resume local authorization after an unlinked SSO login.</summary>
internal sealed class BackOfficeLocalLoginFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.Controller is not BackOfficeLoginController) return;
        var request = context.HttpContext.Request;
        if (!HttpMethods.IsGet(request.Method)) return;
        var original = request.Query["ReturnUrl"].ToString();
        if (string.IsNullOrEmpty(original)) return;

        var authorize = request.PathBase + "/umbraco/management/api/v1/security/back-office/authorize";
        var continuation = BackOfficeSsoFallback.LocalAuthorizeUrl(request, original, authorize, requireExternalProvider: true);
        if (continuation is null) return;

        // Umbraco challenges its password cookie when an external account cannot sign in,
        // but leaves identity_provider in ReturnUrl. The native component reads that query
        // directly, so changing only its MVC model would still retry SSO after a valid password.
        var query = new Dictionary<string, StringValues>(request.Query, StringComparer.OrdinalIgnoreCase)
        {
            ["ReturnUrl"] = continuation,
        };
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.Result = new RedirectResult(request.PathBase + request.Path + QueryString.Create(query));
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
