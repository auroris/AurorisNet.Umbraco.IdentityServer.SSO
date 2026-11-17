using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Infrastructure.Manifest;

namespace IdentityServer.Sso.Tests;

[Collection(nameof(SiteCollection))]
public sealed class ExternalServerSiteTests
{
    [Fact]
    public async Task An_external_server_for_members_only_registers_one_provider_and_no_embedded_server()
    {
        await using var site = await TestSite.StartAsync(new Dictionary<string, string?>
        {
            ["IdentityServer:Enabled"] = "true",
            ["IdentityServer:Name"] = "Domain",
            // A reserved name that never resolves, so discovery fails at once.
            ["IdentityServer:Authority"] = "https://idp.invalid",
            ["IdentityServer:ClientId"] = "sso-site",
            ["IdentityServer:BackOfficeEnabled"] = "false",
        }, TestContext.Current.CancellationToken);

        var schemes = site.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        Assert.Null(await schemes.GetSchemeAsync("Umbraco.Domain"));
        var member = await schemes.GetSchemeAsync("UmbracoMembers.Domain");
        Assert.NotNull(member);
        Assert.Equal(typeof(IdentityServerLoginHandler), member.HandlerType);
        var options = site.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get("UmbracoMembers.Domain");
        Assert.Equal("https://idp.invalid", options.Authority);

        // Nothing uses the embedded server, so it is left out and its paths fall through to Umbraco.
        Assert.False(site.Services.GetRequiredService<IOptions<EmbeddedIdentityServerSettings>>().Value.Enabled);
        Assert.Null(site.Services.GetService<EmbeddedIdentityServerHost>());
        using var client = site.CreateClient();
        using (var authorize = await client.GetAsync("/connect/authorize?client_id=anything", TestContext.Current.CancellationToken))
        {
            Assert.NotEqual(HttpStatusCode.BadRequest, authorize.StatusCode);
            Assert.NotEqual(HttpStatusCode.Unauthorized, authorize.StatusCode);
        }

        // The backoffice login screen has no button to show.
        var reader = site.Services.GetServices<IPackageManifestReader>().OfType<SsoPackageManifestReader>().Single();
        Assert.Empty(await reader.ReadPackageManifestsAsync());

        // An unreachable identity server sends the member back to the page with the password form and the reason.
        using var response = await client.GetAsync("/umbraco/surface/MemberLogin/Login?provider=UmbracoMembers.Domain&returnUrl="
            + Uri.EscapeDataString("/private/?id=4"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = new Uri(site.Address, response.Headers.Location!);
        Assert.Equal("/private/", location.AbsolutePath);
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("4", query["id"].ToString());
        Assert.Equal("1", query[SsoLoginUrls.LocalParameter].ToString());
        Assert.Contains("unavailable", query[SsoRefusal.DeniedParameter].ToString());
    }

    [Fact]
    public async Task A_login_flow_that_names_a_switched_off_provider_stops_the_site_at_startup()
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(() => TestSite.StartAsync(new Dictionary<string, string?>
        {
            ["IdentityServer:Enabled"] = "false",
            ["BackOfficeLogin:DefaultFlow"] = "IdentityServer",
        }, TestContext.Current.CancellationToken));
        Assert.Contains("BackOfficeLogin selects IdentityServer", error.ToString());
    }
}
