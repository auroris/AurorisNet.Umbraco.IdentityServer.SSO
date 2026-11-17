using System.Diagnostics;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;

namespace IdentityServer.Sso.Tests;

/// <summary>
/// A normally composed Umbraco site with SQLite and Kestrel that adds single sign-on the way the README tells a site
/// to: <c>AddSingleSignOn()</c> after <c>AddComposers()</c>. Each test passes the settings it needs.
/// </summary>
internal sealed class TestSite(WebApplication app, string root) : IAsyncDisposable
{
    public const string AdminEmail = "sso-admin@example.test";
    public const string AdminPassword = "Installed-only-Test5278!";

    public IServiceProvider Services => app.Services;
    public string Root => root;
    public Uri Address { get; } = new(app.Services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()!.Addresses.Single());

    /// <summary>The address without a trailing slash, as the identity server writes it into its discovery document.</summary>
    public string Origin => Address.GetLeftPart(UriPartial.Authority);

    /// <summary>
    /// Starts a site in a new temporary folder, installing it unattended. <paramref name="settings"/> are added
    /// after the test defaults, so they override them.
    /// </summary>
    public static async Task<TestSite> StartAsync(IDictionary<string, string?> settings, CancellationToken cancellationToken)
    {
        var root = Directory.CreateTempSubdirectory("sso-test-").FullName;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(TestSite).Assembly.GetName().Name,
            ContentRootPath = root,
            EnvironmentName = "Production",
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:umbracoDbDSN"] = $"Data Source={Path.Combine(root, "umbraco.db")};Pooling=False",
            ["ConnectionStrings:umbracoDbDSN_ProviderName"] = "Microsoft.Data.Sqlite",
            ["Umbraco:CMS:Global:Id"] = "1c6b0d0e-3f7a-4e57-9a43-5f0b8a2d7c21",
            ["Umbraco:CMS:Global:UseHttps"] = "false",
            ["Umbraco:CMS:Global:VersionCheckPeriod"] = "0",
            ["Umbraco:CMS:WebRouting:ApplicationUrlDetection"] = "FirstRequest",
            ["Umbraco:CMS:TypeFinder:AdditionalAssemblyExclusionEntries:0"] = "xunit.",
            ["Umbraco:CMS:Unattended:InstallUnattended"] = "true",
            ["Umbraco:CMS:Unattended:UpgradeUnattended"] = "true",
            ["Umbraco:CMS:Unattended:UnattendedUserName"] = "SSO administrator",
            ["Umbraco:CMS:Unattended:UnattendedUserEmail"] = AdminEmail,
            ["Umbraco:CMS:Unattended:UnattendedUserPassword"] = AdminPassword,
            // Usernames are Windows account names here, as on the intranet sites this package serves.
            ["Umbraco:CMS:Security:UsernameIsEmail"] = "false",
        });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.CreateUmbracoBuilder().AddBackOffice().AddWebsite().AddComposers().AddSingleSignOn().Build();

        var app = builder.Build();
        try
        {
            await app.BootUmbracoAsync();
            app.UseUmbraco().WithMiddleware(u => { u.UseBackOffice(); u.UseWebsite(); })
                .WithEndpoints(u => { u.UseBackOfficeEndpoints(); u.UseWebsiteEndpoints(); });
            await app.StartAsync(cancellationToken);
            // Umbraco installs in a hosted service after the server starts, then raises its started notification.
            var installer = app.Services.GetServices<IHostedService>().OfType<BackgroundService>()
                .SingleOrDefault(service => service.GetType().Name == "UnattendedUpgradeBackgroundService");
            if (installer?.ExecuteTask is { } installation)
                await installation.WaitAsync(TimeSpan.FromMinutes(3), cancellationToken);
            var runtime = app.Services.GetRequiredService<IRuntimeState>();
            var ready = ContentRoutingReady(app.Services);
            var waited = Stopwatch.StartNew();
            while ((runtime.Level != RuntimeLevel.Run || !ready()) && runtime.Level != RuntimeLevel.BootFailed
                && waited.Elapsed < TimeSpan.FromMinutes(1))
                await Task.Delay(100, cancellationToken);
            Assert.True(runtime.Level == RuntimeLevel.Run, runtime.BootFailedException?.ToString() ?? runtime.Level.ToString());
            Assert.True(ready(), "Umbraco did not finish initializing its content caches.");
            return new TestSite(app, root);
        }
        catch
        {
            await app.StopAsync(CancellationToken.None);
            await app.DisposeAsync();
            await Serilog.Log.CloseAndFlushAsync();
            DeleteRoot(root);
            throw;
        }
    }

    /// <summary>
    /// Whether Umbraco has initialized its content caches. Umbraco 18.1 added IContentRoutingReadiness; earlier releases
    /// are ready once the runtime level is Run. Reflection keeps the tests building against every supported release.
    /// </summary>
    private static Func<bool> ContentRoutingReady(IServiceProvider services)
    {
        var type = typeof(IRuntimeState).Assembly.GetType("Umbraco.Cms.Core.Routing.IContentRoutingReadiness");
        if (type is null) return () => true;
        var readiness = services.GetRequiredService(type);
        var isReady = type.GetProperty("IsReady")!;
        return () => (bool)isReady.GetValue(readiness)!;
    }

    /// <summary>A browser stand-in that keeps no cookies and follows no redirects.</summary>
    public HttpClient CreateClient() => new(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
    {
        BaseAddress = Address, Timeout = TimeSpan.FromSeconds(60),
    };

    /// <summary>Creates an approved backoffice user in the Editors group, as an administrator would.</summary>
    public async Task<IUser> CreateUserAsync(string username, string email, string name)
    {
        using var scope = Services.CreateScope();
        var created = await scope.ServiceProvider.GetRequiredService<IUserService>().CreateAsync(Constants.Security.SuperUserKey, new UserCreateModel
        {
            UserName = username,
            Email = email,
            Name = name,
            Kind = UserKind.Default,
            UserGroupKeys = new HashSet<Guid> { Constants.Security.EditorGroupKey },
        }, approveUser: true);
        Assert.True(created.Success, created.Status.ToString());
        return created.Result.CreatedUser!;
    }

    /// <summary>Creates an approved member of Umbraco's default member type, as an administrator would.</summary>
    public IMember CreateMember(string username, string email, string name)
    {
        var members = Services.GetRequiredService<IMemberService>();
        var member = members.CreateMemberWithIdentity(username, email, name, Constants.Conventions.MemberTypes.DefaultAlias, isApproved: true);
        members.Save(member);
        return member;
    }

    /// <summary>Runs the account policy in a scope of its own.</summary>
    public async Task<ProvisioningResult> ProvisionAsync(Func<AccountProvisioning, Task<ProvisioningResult>> provision)
    {
        using var scope = Services.CreateScope();
        return await provision(scope.ServiceProvider.GetRequiredService<AccountProvisioning>());
    }

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync(CancellationToken.None);
        await app.DisposeAsync();
        await Serilog.Log.CloseAndFlushAsync();
        DeleteRoot(root);
    }

    /// <summary>Deletes a folder from <see cref="StartAsync"/>, and nothing else.</summary>
    private static void DeleteRoot(string root)
    {
        var fullRoot = Path.GetFullPath(root);
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!fullRoot.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(fullRoot).StartsWith("sso-test-", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unexpected test directory: {fullRoot}");
        // SQLite can hold the file briefly after the site stops.
        for (var attempt = 0; ; attempt++)
        {
            try { Directory.Delete(fullRoot, recursive: true); return; }
            catch (IOException) when (attempt < 20) { Thread.Sleep(250); }
            catch (UnauthorizedAccessException) when (attempt < 20) { Thread.Sleep(250); }
        }
    }
}
