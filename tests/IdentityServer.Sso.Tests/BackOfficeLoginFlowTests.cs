using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Umbraco.Cms.Core.Configuration.Models;

namespace IdentityServer.Sso.Tests;

public sealed class BackOfficeLoginFlowTests
{
    [Theory]
    [InlineData("DefaultFlow")]
    [InlineData("DebugFlow")]
    public void The_removed_Windows_flow_is_not_a_valid_configuration_value(string setting)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BackOfficeLogin:" + setting] = "Windows",
        }).Build();
        Assert.Throws<InvalidOperationException>(() => configuration.GetSection("BackOfficeLogin").Get<BackOfficeLoginSettings>());
        Assert.False(Enum.IsDefined((BackOfficeLoginFlow)2));
    }

    [Theory]
    [InlineData(false, "Form", "Debug", null)]
    [InlineData(false, "Auto", "Debug", "Umbraco.Domain")]
    [InlineData(false, "IdentityServer", "Debug", "Umbraco.Domain")]
    [InlineData(false, "Debug", "Debug", null)]
    [InlineData(true, "Form", "Debug", null)]
    [InlineData(true, "Auto", "Debug", null)]
    [InlineData(true, "Form", "IdentityServer", "Umbraco.Domain")]
    public async Task The_manifest_offers_the_provider_and_redirects_only_for_the_selected_flow(
        bool debug, string defaultFlow, string debugFlow, string? redirectScheme)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BackOfficeLogin:DefaultFlow"] = defaultFlow,
            ["BackOfficeLogin:DebugFlow"] = debugFlow,
        }).Build();
        var login = Options.Create(configuration.GetSection("BackOfficeLogin").Get<BackOfficeLoginSettings>()!);
        var hosting = Options.Create(new HostingSettings { Debug = debug });
        var identityServer = Options.Create(new IdentityServerSettings { Enabled = true, Name = "Domain" });
        var reader = new SsoPackageManifestReader(new SsoProviderCatalog(identityServer), login, hosting, identityServer);

        var manifest = Assert.Single(await reader.ReadPackageManifestsAsync());
        var extension = Assert.Single(manifest.Extensions!.Cast<JsonElement>());
        Assert.Equal("authProvider", extension.GetProperty("type").GetString());
        Assert.Equal("Umbraco.Domain", extension.GetProperty("forProviderName").GetString());
        Assert.Equal("Domain", extension.GetProperty("meta").GetProperty("label").GetString());
        Assert.True(extension.GetProperty("meta").GetProperty("linking").GetProperty("allowManualLinking").GetBoolean());
        Assert.Equal(redirectScheme is not null, extension.GetProperty("meta").GetProperty("behavior").GetProperty("autoRedirect").GetBoolean());
    }

    [Fact]
    public async Task The_manifest_is_empty_without_an_available_backoffice_provider()
    {
        var login = Options.Create(new BackOfficeLoginSettings());
        var hosting = Options.Create(new HostingSettings());
        foreach (var settings in new[]
        {
            new IdentityServerSettings { Enabled = false },
            new IdentityServerSettings { Enabled = true, BackOfficeEnabled = false },
        })
        {
            var identityServer = Options.Create(settings);
            var reader = new SsoPackageManifestReader(new SsoProviderCatalog(identityServer), login, hosting, identityServer);
            Assert.Empty(await reader.ReadPackageManifestsAsync());
        }

        // The embedded server needs Windows authentication; where the web server has none, the button is left out.
        var embedded = Options.Create(new IdentityServerSettings { Enabled = true });
        var catalog = new SsoProviderCatalog(embedded, new WindowsAuthenticationAvailability([new DisabledIntegratedAuth()]));
        Assert.Empty(catalog.BackOfficeLogins);
        Assert.Single(catalog.BackOffice);
        Assert.Empty(await new SsoPackageManifestReader(catalog, login, hosting, embedded).ReadPackageManifestsAsync());
        // An external identity server authenticates people itself.
        var external = Options.Create(new IdentityServerSettings { Enabled = true, Authority = "https://idp.example" });
        Assert.Single(new SsoProviderCatalog(external, new WindowsAuthenticationAvailability([new DisabledIntegratedAuth()])).BackOfficeLogins);
    }

    private sealed class DisabledIntegratedAuth : Microsoft.AspNetCore.Hosting.Server.IServerIntegratedAuth
    {
        public bool IsEnabled => false;
        public string AuthenticationScheme => "Windows";
    }

    [Theory]
    [InlineData(BackOfficeLoginFlow.IdentityServer, false, true)]
    [InlineData(BackOfficeLoginFlow.IdentityServer, true, false)]
    public void Automatic_sign_in_requires_an_enabled_backoffice_provider(BackOfficeLoginFlow flow, bool enabled, bool backOfficeEnabled)
    {
        var settings = new BackOfficeLoginSettings { DefaultFlow = flow };
        Assert.Throws<InvalidOperationException>(() => settings.GetAutoRedirectScheme(false,
            new IdentityServerSettings { Enabled = enabled, BackOfficeEnabled = backOfficeEnabled }));
    }

    [Theory]
    [InlineData(true, true, "Umbraco.Domain")]
    [InlineData(false, true, null)]
    [InlineData(true, false, null)]
    public void Defaults_prefer_available_identity_server_in_production_and_keep_debug_password_flow(
        bool enabled, bool backOfficeEnabled, string? expectedScheme)
    {
        var settings = new BackOfficeLoginSettings();
        var identity = new IdentityServerSettings { Enabled = enabled, BackOfficeEnabled = backOfficeEnabled, Name = "Domain" };
        Assert.Equal(expectedScheme, settings.GetAutoRedirectScheme(false, identity));
        Assert.Equal(BackOfficeLoginFlow.Debug, settings.GetEffectiveFlow(true));
        Assert.Null(settings.GetAutoRedirectScheme(true, identity));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("/mounted", false)]
    [InlineData("/mounted", true)]
    public async Task Fallback_preserves_the_authorization_request_without_repeating_the_external_provider(string pathBase, bool absolute)
    {
        var context = Context(pathBase);
        var authorize = pathBase + "/umbraco/management/api/v1/security/back-office/authorize";
        var request = authorize + "?client_id=umbraco-back-office&redirect_uri=https%3A%2F%2Fapp.example%2Fumbraco%2Foauth_complete&state=state%2Bvalue&code_challenge=challenge&code_challenge_method=S256&identity_provider=Umbraco.Domain&scope=one&scope=two";
        await BackOfficeSsoFallback.RedirectAsync(context,
            new AuthenticationProperties { RedirectUri = (absolute ? "https://app.example" : "") + request }, "Identity server unavailable");

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        var location = new Uri(new Uri("https://app.example"), context.Response.Headers.Location.ToString());
        Assert.Equal(pathBase + "/umbraco/login", location.AbsolutePath);
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("Identity server unavailable", query[SsoRefusal.DeniedParameter].ToString());
        var continuation = new Uri(new Uri("https://app.example"), query["ReturnUrl"].ToString());
        Assert.Equal(authorize, continuation.AbsolutePath);
        var parameters = QueryHelpers.ParseQuery(continuation.Query);
        Assert.False(parameters.ContainsKey("identity_provider"));
        Assert.Equal("state+value", parameters["state"].ToString());
        Assert.Equal("challenge", parameters["code_challenge"].ToString());
        Assert.Equal("S256", parameters["code_challenge_method"].ToString());
        Assert.Equal(new[] { "one", "two" }, parameters["scope"].ToArray());
        Assert.Equal("https://app.example/umbraco/oauth_complete", parameters["redirect_uri"].ToString());
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://other.example/umbraco/management/api/v1/security/back-office/authorize?state=x")]
    [InlineData("//other.example/umbraco/management/api/v1/security/back-office/authorize?state=x")]
    [InlineData("/umbraco/")]
    [InlineData("/umbraco/management/api/v1/security/back-office/authorize?state=x#fragment")]
    public async Task Missing_or_unsafe_callback_state_opens_the_native_provider_chooser_without_auto_redirect(string? redirectUri)
    {
        var context = Context("");
        context.Request.Path = "/signin-oidc/backoffice";
        await BackOfficeSsoFallback.RedirectAsync(context, new AuthenticationProperties { RedirectUri = redirectUri }, "Sign-in failed");
        var location = new Uri(new Uri("https://app.example"), context.Response.Headers.Location.ToString());
        Assert.Equal("/umbraco/logout", location.AbsolutePath);
        Assert.False(QueryHelpers.ParseQuery(location.Query).ContainsKey("ReturnUrl"));
    }

    [Theory]
    [InlineData("", "Umbraco.Domain")]
    [InlineData("/mounted", "Umbraco.Domain")]
    [InlineData("/mounted", "UmbracoMembers.Domain")]
    public async Task Executed_discovery_failure_returns_to_the_correct_sign_in_flow(string pathBase, string scheme)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication().AddCookie("local").AddOpenIdConnect(scheme, options =>
        {
            options.SignInScheme = "local";
            options.ClientId = "test-client";
            options.ConfigurationManager = new UnavailableMetadata();
        });
        services.AddTransient<IdentityServerLoginHandler>();
        await using var provider = services.BuildServiceProvider();
        var context = Context(pathBase);
        context.RequestServices = provider;
        context.Request.Path = "/umbraco/management/api/v1/security/back-office/authorize";
        context.Request.QueryString = new QueryString("?state=keep-me&code_challenge=challenge&identity_provider=" + scheme);
        var properties = scheme.StartsWith("UmbracoMembers.", StringComparison.Ordinal)
            ? new AuthenticationProperties { RedirectUri = pathBase + "/app/callback?returnUrl=" + Uri.EscapeDataString(pathBase + "/orders/?id=42") }
            : new AuthenticationProperties();
        var handler = provider.GetRequiredService<IdentityServerLoginHandler>();
        await handler.InitializeAsync(new AuthenticationScheme(scheme, "Domain", typeof(IdentityServerLoginHandler)), context);

        await handler.ChallengeAsync(properties);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        var location = new Uri(new Uri("https://app.example"), context.Response.Headers.Location.ToString());
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Contains("unavailable", query[SsoRefusal.DeniedParameter].ToString());
        if (scheme.StartsWith("UmbracoMembers.", StringComparison.Ordinal))
        {
            Assert.Equal(pathBase + "/orders/", location.AbsolutePath);
            Assert.Equal("42", query["id"].ToString());
        }
        else
        {
            Assert.Equal(pathBase + "/umbraco/login", location.AbsolutePath);
            var continuation = new Uri(new Uri("https://app.example"), query["ReturnUrl"].ToString());
            var authorize = QueryHelpers.ParseQuery(continuation.Query);
            Assert.Equal("keep-me", authorize["state"].ToString());
            Assert.Equal("challenge", authorize["code_challenge"].ToString());
            Assert.False(authorize.ContainsKey("identity_provider"));
        }
    }

    private static DefaultHttpContext Context(string pathBase)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("app.example");
        context.Request.PathBase = pathBase;
        return context;
    }

    private sealed class UnavailableMetadata : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancellationToken)
            => throw new HttpRequestException("The identity server could not be reached.");

        public void RequestRefresh() { }
    }
}
