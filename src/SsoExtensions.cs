using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Infrastructure.Manifest;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Notifications;

namespace IdentityServer.Sso;

/// <summary>Registers account services and the OpenID Connect sign-in provider.</summary>
public static class SsoExtensions
{
    /// <summary><see cref="AddSingleSignOn(IUmbracoBuilder, IConfiguration)"/> with the site's own configuration.</summary>
    public static IUmbracoBuilder AddSingleSignOn(this IUmbracoBuilder builder)
        => builder.AddSingleSignOn(builder.Config);

    /// <summary>
    /// Adds single sign-on and the account policy: the OpenID Connect provider for the backoffice and for members
    /// from the <c>IdentityServer</c> section, the embedded identity server from <c>EmbeddedIdentityServer</c>, the
    /// initial backoffice flow from <c>BackOfficeLogin</c>, and the configured accounts and retirement on delete from
    /// <c>Accounts</c>. Call it once, after <c>AddComposers()</c>.
    /// </summary>
    public static IUmbracoBuilder AddSingleSignOn(this IUmbracoBuilder builder, IConfiguration configuration)
    {
        builder.Services.AddOptions<AccountsSettings>()
            .Bind(configuration.GetSection(AccountsSettings.SectionName))
            .ValidateOnStart();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AccountsSettings>, AccountsSettingsValidator>());
        builder.Services.Configure<IdentityServerSettings>(configuration.GetSection(IdentityServerSettings.SectionName));
        builder.Services.AddOptions<BackOfficeLoginSettings>()
            .Bind(configuration.GetSection(BackOfficeLoginSettings.SectionName))
            .Validate(settings => Enum.IsDefined(settings.DefaultFlow)
                && (settings.DebugFlow is null || Enum.IsDefined(settings.DebugFlow.Value)),
                "BackOfficeLogin flows must be Auto, Form, IdentityServer, or Debug.")
            .ValidateOnStart();
        builder.Services.AddScoped<AccountProvisioning>();
        builder.Services.AddSingleton<SsoProviderCatalog>();
        // Whether Windows authentication works here decides which providers the login screens offer.
        builder.Services.TryAddSingleton<WindowsAuthenticationAvailability>();
        builder.AddNotificationHandler<UmbracoApplicationStartedNotification, WindowsAuthenticationStartupLog>();
        // The groups, members and backoffice users the Accounts section lists, created once the site runs.
        builder.Services.TryAddTransient<ConfiguredAccounts>();
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, ConfiguredAccounts>();
        builder.Services.AddSingleton<IPackageManifestReader, SsoPackageManifestReader>();
        builder.Services.Configure<MvcOptions>(options => options.Filters.Add(new BackOfficeLocalLoginFilter()));

        // Provider registration happens while services are being built, so the sections are read directly here.
        var identityServer = configuration.GetSection(IdentityServerSettings.SectionName).Get<IdentityServerSettings>() ?? new IdentityServerSettings();
        builder.Services.AddOptions<BackOfficeLoginSettings>()
            .Validate<Microsoft.Extensions.Options.IOptions<HostingSettings>>((settings, hosting) =>
            {
                // Catch an unavailable default provider at startup, with an actionable configuration error.
                settings.GetAutoRedirectScheme(hosting.Value.Debug, identityServer);
                return true;
            });
        return builder
            .AddIdentityServerLogin(identityServer)
            .AddEmbeddedIdentityServer(configuration)
            .AddAccountRetirement(configuration);
    }
}
