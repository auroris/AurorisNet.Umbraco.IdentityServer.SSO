using System.Net;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;
using Umbraco.Cms.Infrastructure.Manifest;

namespace IdentityServer.Sso.Tests;

/// <summary>Sites run one at a time, because Umbraco keeps static state.</summary>
[CollectionDefinition(nameof(SiteCollection), DisableParallelization = true)]
public sealed class SiteCollection;

/// <summary>
/// One site that signs in through its own embedded identity server, with another application registered as a client
/// and accounts listed in the <c>Accounts</c> section.
/// </summary>
public sealed class EmbeddedServerSite : IAsyncLifetime
{
    public const string ClientId = "test-client";
    public const string ClientSecret = "Local-test-only-client-secret-91";
    public const string RedirectUri = "https://client.example/callback";
    public const string SiteClientId = "sso-site";

    private TestSite? site;

    internal TestSite Site => site ?? throw new InvalidOperationException("The site has not started.");

    public async ValueTask InitializeAsync() => site = await TestSite.StartAsync(new Dictionary<string, string?>
    {
        ["IdentityServer:Enabled"] = "true",
        ["IdentityServer:Name"] = "Domain",
        ["IdentityServer:ClientId"] = SiteClientId,
        ["EmbeddedIdentityServer:Clients:0:ClientId"] = ClientId,
        ["EmbeddedIdentityServer:Clients:0:ClientSecret"] = ClientSecret,
        ["EmbeddedIdentityServer:Clients:0:RedirectUris:0"] = RedirectUri,
        ["Accounts:EnsureMemberGroups:0"] = "Sso Members",
        ["Accounts:EnsureUserGroups:0"] = "Sso Editors",
        ["Accounts:EnsureMembers:0:Email"] = "seeded.member@example.test",
        ["Accounts:EnsureMembers:0:MemberGroups:0"] = "Sso Members",
        ["Accounts:EnsureBackOfficeUsers:0:Email"] = "seeded.user@example.test",
        ["Accounts:EnsureBackOfficeUsers:0:Name"] = "Seeded User",
        ["Accounts:EnsureBackOfficeUsers:0:UserGroups:0"] = "ssoEditors",
    }, TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (site is not null) await site.DisposeAsync();
    }
}

[Collection(nameof(SiteCollection))]
public sealed class EmbeddedServerSiteTests(EmbeddedServerSite fixture) : IClassFixture<EmbeddedServerSite>
{
    private const string Domain = "Umbraco.Domain";
    private const string DomainMembers = "UmbracoMembers.Domain";

    private TestSite Site => fixture.Site;

    [Fact]
    public async Task Both_audiences_get_an_OpenID_Connect_provider_that_reads_metadata_in_process()
    {
        var schemes = Site.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        foreach (var (scheme, audience) in new[] { (Domain, "backoffice"), (DomainMembers, "member") })
        {
            var registered = await schemes.GetSchemeAsync(scheme);
            Assert.NotNull(registered);
            Assert.Equal(typeof(IdentityServerLoginHandler), registered.HandlerType);
            Assert.Equal("Domain", registered.DisplayName);
            var options = Site.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(scheme);
            Assert.Equal(EmbeddedServerSite.SiteClientId, options.ClientId);
            Assert.Null(options.Authority);
            Assert.IsType<EmbeddedIdentityServerConfigurationManager>(options.ConfigurationManager);
            Assert.Equal("/signin-oidc/" + audience, options.CallbackPath.Value);
            Assert.Equal(OpenIdConnectResponseType.Code, options.ResponseType);
            Assert.Equal(OpenIdConnectResponseMode.Query, options.ResponseMode);
            Assert.Equal(SameSiteMode.Lax, options.CorrelationCookie.SameSite);
            Assert.Equal(["email", "openid", "profile", "roles"], options.Scope.Order(StringComparer.Ordinal));
        }

        // The login screen learns about the backoffice provider from a manifest built from the settings. Kestrel can
        // always negotiate, so the Auto flow redirects to it.
        var reader = Site.Services.GetServices<IPackageManifestReader>().OfType<SsoPackageManifestReader>().Single();
        var extension = Assert.Single(Assert.Single(await reader.ReadPackageManifestsAsync()).Extensions!.Cast<JsonElement>());
        Assert.Equal(Domain, extension.GetProperty("forProviderName").GetString());
        Assert.True(extension.GetProperty("meta").GetProperty("behavior").GetProperty("autoRedirect").GetBoolean());
    }

    [Fact]
    public async Task The_discovery_document_follows_the_request_address()
    {
        using var client = Site.CreateClient();
        var discovery = await GetJsonAsync(client, "/.well-known/openid-configuration");
        Assert.Equal(Site.Origin + "/connect/authorize", discovery.GetProperty("authorization_endpoint").GetString());
        Assert.Equal(Site.Origin + "/connect/token", discovery.GetProperty("token_endpoint").GetString());

        // The site's own handler reads the same metadata from the server in this process, for the current request.
        var manager = Site.Services.GetRequiredService<EmbeddedIdentityServerConfigurationManager>();
        var accessor = Site.Services.GetRequiredService<IHttpContextAccessor>();
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("sso.example");
        context.Request.PathBase = "/wing";
        accessor.HttpContext = context;
        try
        {
            var configuration = await manager.GetConfigurationAsync(TestContext.Current.CancellationToken);
            Assert.Equal("https://sso.example/wing/connect/authorize", configuration.AuthorizationEndpoint);
            Assert.Equal("https://sso.example/wing/connect/token", configuration.TokenEndpoint);
            Assert.NotEmpty(configuration.SigningKeys);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    [Fact]
    public async Task The_server_accepts_only_registered_clients_with_exact_callbacks_and_PKCE()
    {
        using var client = Site.CreateClient();
        foreach (var invalid in new[]
        {
            AuthorizationUrl("not-registered", EmbeddedServerSite.RedirectUri),
            AuthorizationUrl(EmbeddedServerSite.ClientId, "https://other.example/callback"),
            AuthorizationUrl(EmbeddedServerSite.SiteClientId, "https://other.example/signin-oidc/backoffice"),
            AuthorizationUrl(EmbeddedServerSite.SiteClientId, Site.Origin + "/some-other-callback"),
        })
        {
            using var response = await client.GetAsync(QueryHelpers.AddQueryString(invalid, SsoLoginUrls.LocalParameter, "1"), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Null(response.Headers.Location);
        }

        // The site's own client needs no registration. Without Windows credentials it is answered through its callback.
        var ownCallback = Site.Origin + "/signin-oidc/backoffice";
        using (var own = await client.GetAsync(QueryHelpers.AddQueryString(
            AuthorizationUrl(EmbeddedServerSite.SiteClientId, ownCallback), SsoLoginUrls.LocalParameter, "1"), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Redirect, own.StatusCode);
            Assert.StartsWith(ownCallback + "?", own.Headers.Location!.ToString());
            Assert.Equal("access_denied", QueryHelpers.ParseQuery(own.Headers.Location.Query)["error"].ToString());
        }

        // A registered client without Windows credentials is invited to negotiate.
        using (var challenge = await client.GetAsync(AuthorizationUrl(EmbeddedServerSite.ClientId, EmbeddedServerSite.RedirectUri), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, challenge.StatusCode);
            Assert.Contains(challenge.Headers.WwwAuthenticate, header => header.Scheme == "Negotiate");
        }

        var withoutPkce = QueryHelpers.ParseQuery(new Uri(Site.Address, AuthorizationUrl(EmbeddedServerSite.ClientId, EmbeddedServerSite.RedirectUri)).Query)
            .Where(parameter => !parameter.Key.StartsWith("code_challenge", StringComparison.Ordinal))
            .ToDictionary(parameter => parameter.Key, parameter => (string?)parameter.Value.ToString());
        using var missingPkce = await client.GetAsync("/connect/authorize" + QueryString.Create(withoutPkce), TestContext.Current.CancellationToken);
        var error = missingPkce.Headers.Location?.Query ?? await missingPkce.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("invalid_request", error);
    }

    [Fact]
    public async Task A_registered_client_gets_tokens_for_the_current_Windows_user()
    {
        // Negotiate falls back to NTLM on the loopback address, so the current Windows account signs in without
        // Kerberos; other platforms have no current Windows account.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Signing in needs Windows authentication for the account running the tests.");
        using var windows = new HttpClient(new HttpClientHandler { UseDefaultCredentials = true, AllowAutoRedirect = false })
        {
            BaseAddress = Site.Address, Timeout = TimeSpan.FromSeconds(60),
        };
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        using var authorize = await windows.GetAsync(AuthorizationUrl(EmbeddedServerSite.ClientId, EmbeddedServerSite.RedirectUri, verifier), TestContext.Current.CancellationToken);
        var body = await authorize.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(authorize.StatusCode == HttpStatusCode.Redirect, $"Authorize answered {(int)authorize.StatusCode}: {body}");
        var code = QueryHelpers.ParseQuery(authorize.Headers.Location!.Query)["code"].ToString();
        Assert.NotEmpty(code);

        using var client = Site.CreateClient();
        using var token = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = EmbeddedServerSite.RedirectUri,
            ["client_id"] = EmbeddedServerSite.ClientId, ["client_secret"] = EmbeddedServerSite.ClientSecret, ["code_verifier"] = verifier,
        }), TestContext.Current.CancellationToken);
        var json = await token.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(token.IsSuccessStatusCode, json);
        var idToken = new JsonWebToken(JsonDocument.Parse(json).RootElement.GetProperty("id_token").GetString());
#pragma warning disable CA1416 // Skipped above on other platforms.
        Assert.Equal(WindowsIdentity.GetCurrent().User!.Value, idToken.Subject);
#pragma warning restore CA1416
    }

    [Fact]
    public async Task The_password_form_resumes_authorization_without_the_external_provider()
    {
        const string authorize = "/umbraco/management/api/v1/security/back-office/authorize";
        var original = authorize + "?client_id=umbraco-back-office&state=keep-me&code_challenge=challenge&identity_provider=" + Domain;
        using var client = Site.CreateClient();
        using var response = await client.GetAsync("/umbraco/login?ReturnUrl=" + Uri.EscapeDataString(original), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = new Uri(Site.Address, response.Headers.Location!);
        Assert.Equal("/umbraco/login", location.AbsolutePath);
        var continuation = new Uri(Site.Address, QueryHelpers.ParseQuery(location.Query)["ReturnUrl"].ToString());
        Assert.Equal(authorize, continuation.AbsolutePath);
        var query = QueryHelpers.ParseQuery(continuation.Query);
        Assert.False(query.ContainsKey("identity_provider"));
        Assert.Equal("keep-me", query["state"].ToString());
        Assert.Equal("challenge", query["code_challenge"].ToString());

        // A request without an external provider is the password form's own and is left alone.
        using var plain = await client.GetAsync("/umbraco/login?ReturnUrl=" + Uri.EscapeDataString(authorize + "?state=x"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
    }

    [Fact]
    public async Task A_member_provider_that_is_not_offered_returns_to_the_page_with_the_password_form()
    {
        using var client = Site.CreateClient();
        using var response = await client.GetAsync("/umbraco/surface/MemberLogin/Login?provider=UmbracoMembers.Unknown&returnUrl="
            + Uri.EscapeDataString("/private/?id=4"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = new Uri(Site.Address, response.Headers.Location!);
        Assert.Equal("/private/", location.AbsolutePath);
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("4", query["id"].ToString());
        Assert.Equal("1", query[SsoLoginUrls.LocalParameter].ToString());
        Assert.Equal("That sign-in provider is not available.", query[SsoRefusal.DeniedParameter].ToString());
    }

    [Fact]
    public async Task The_configured_groups_and_accounts_exist_once_the_site_runs()
    {
        await WaitUntilAsync(() => Site.Services.GetRequiredService<IUserService>().GetByEmail("seeded.user@example.test") is not null,
            "the configured backoffice user");
        var users = Site.Services.GetRequiredService<IUserService>();
        var user = users.GetByEmail("seeded.user@example.test")!;
        Assert.Equal("Seeded User", user.Name);
        Assert.Equal("seeded.user@example.test", user.Username);
        // The alias comes from the name; the seed may name it in any case.
        Assert.Contains(user.Groups, group => string.Equals(group.Alias, "ssoEditors", StringComparison.OrdinalIgnoreCase));

        var members = Site.Services.GetRequiredService<IMemberService>();
        var member = members.GetByEmail("seeded.member@example.test");
        Assert.NotNull(member);
        Assert.Equal(["Sso Members"], members.GetAllRoles(member.Id));
        Assert.NotNull(await Site.Services.GetRequiredService<IMemberGroupService>().GetByNameAsync("Sso Members"));
    }

    [Fact]
    public async Task A_pre_created_user_is_locked_to_the_first_Windows_identity_and_retired_on_delete()
    {
        const string first = "S-1-5-21-100-200-300-1001";
        const string other = "S-1-5-21-100-200-300-2002";
        var user = await Site.CreateUserAsync("test.user", "test.user@example.invalid", "Test User");

        // The first sign-in matches the pre-created username regardless of case and links the SID.
        Assert.Equal(ProvisioningResult.Ok, await Site.ProvisionAsync(p => p.ProvisionBackOfficeAsync(Account(first, "DOMAIN\\TEST.USER"), Domain)));
        // A new person given the same username and email is someone else.
        var refused = await Site.ProvisionAsync(p => p.ProvisionBackOfficeAsync(Account(other, "DOMAIN\\Test.User", "test.user@example.invalid"), Domain));
        Assert.False(refused.Allowed);
        Assert.Equal("unauthorized", refused.Status);

        // Deleting the user who is linked keeps the row with its name and frees the username and email.
        var userService = Assert.IsType<RetiringUserService>(Site.Services.GetRequiredService<IUserService>());
        Assert.Equal(UserOperationStatus.Success, await userService.DeleteAsync(Constants.Security.SuperUserKey, user.Key));
        var retired = (await userService.GetAsync(user.Key))!;
        Assert.Equal(first, retired.Username);
        Assert.Equal($"{first}@{AccountRetirement.RetiredEmailDomain}", retired.Email);
        Assert.False(retired.IsApproved);
        Assert.Equal("Test User", retired.Name);
        Assert.Equal(ProvisioningResult.Unauthorized("This account has been retired."),
            await Site.ProvisionAsync(p => p.ProvisionBackOfficeAsync(Account(first, "DOMAIN\\Test.User"), Domain)));

        // A user who never signed in is deleted as Umbraco deletes it.
        var unused = await Site.CreateUserAsync("never.used", "never.used@example.invalid", "Never Used");
        Assert.Equal(UserOperationStatus.Success, await userService.DeleteAsync(Constants.Security.SuperUserKey, unused.Key));
        Assert.Null(await userService.GetAsync(unused.Key));
    }

    [Fact]
    public async Task A_member_linked_to_a_Windows_identity_is_retired_on_delete()
    {
        const string sid = "S-1-5-21-500-600-700-3003";
        var member = Site.CreateMember("member.user", "member.user@example.invalid", "Member User");
        Assert.True((await Site.ProvisionAsync(p => p.ProvisionMemberAsync(Account(sid, "DOMAIN\\Member.User"), DomainMembers))).Allowed);

        using (var scope = Site.Services.CreateScope())
        {
            var editing = scope.ServiceProvider.GetRequiredService<IMemberEditingService>();
            Assert.IsType<RetiringMemberEditingService>(editing);
            Assert.True((await editing.DeleteAsync(member.Key, Constants.Security.SuperUserKey)).Success);
        }

        var retired = Site.Services.GetRequiredService<IMemberService>().GetById(member.Key);
        Assert.NotNull(retired);
        Assert.Equal(sid, retired.Username);
        Assert.False(retired.IsApproved);
        Assert.Equal(ProvisioningResult.Unauthorized("This account has been retired."),
            await Site.ProvisionAsync(p => p.ProvisionMemberAsync(Account(sid, "DOMAIN\\Member.User"), DomainMembers)));
    }

    /// <summary>A sign-in as a provider presents it, with a domain account name such as <c>DOMAIN\Test.User</c>.</summary>
    private static SsoAccount Account(string sid, string accountName, string? email = null)
    {
        var userName = accountName[(accountName.IndexOf('\\') + 1)..];
        return new SsoAccount(sid, accountName, userName, userName.Replace('.', ' '), email, null, null, true, []);
    }

    private static string AuthorizationUrl(string clientId, string redirectUri, string? verifier = null)
        => "/connect/authorize" + QueryString.Create(new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid",
            ["state"] = "state-1",
            ["nonce"] = "nonce-1",
            ["code_challenge"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier ?? Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32))))),
            ["code_challenge_method"] = "S256",
        });

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{path} answered {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>Polls for up to 15 seconds, for work the started notification does in the background.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        for (var attempt = 0; attempt < 150; attempt++)
        {
            if (condition()) return;
            await Task.Delay(100);
        }
        Assert.Fail($"Waited 15 seconds for {what}.");
    }
}
