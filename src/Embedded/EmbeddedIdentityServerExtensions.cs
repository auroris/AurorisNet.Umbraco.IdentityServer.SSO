using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Web.Common.ApplicationBuilder;

namespace IdentityServer.Sso;

/// <summary>
/// Hosts IdentityServer.Core, from the <c>AurorisNet.IdentityServer</c> package, alongside Umbraco's own authentication
/// server.
/// </summary>
public static class EmbeddedIdentityServerExtensions
{
    /// <summary>
    /// Binds the <c>EmbeddedIdentityServer</c> section and, when <see cref="EmbeddedIdentityServerSettings.IsEnabled"/>,
    /// serves the server's endpoints ahead of Umbraco's routing. Called by <see cref="SsoExtensions.AddSingleSignOn(IUmbracoBuilder, IConfiguration)"/>.
    /// </summary>
    public static IUmbracoBuilder AddEmbeddedIdentityServer(this IUmbracoBuilder builder, IConfiguration configuration)
    {
        builder.Services.AddOptions<EmbeddedIdentityServerSettings>()
            .Bind(configuration.GetSection(EmbeddedIdentityServerSettings.SectionName))
            // An unset Enabled follows what uses the server; the same answer decides the registration below.
            .PostConfigure(settings => settings.Enabled = EmbeddedIdentityServerSettings.IsEnabled(configuration))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<EmbeddedIdentityServerSettings>, EmbeddedIdentityServerSettingsValidator>();
        // Old versions stored these clients in Umbraco's database. Keep those registrations from
        // being used at Umbraco's own endpoints after an upgrade, even if removed from configuration.
        builder.Services.AddOpenIddict().AddServer(options =>
        {
            options.AddEventHandler<OpenIddict.Server.OpenIddictServerEvents.ValidateAuthorizationRequestContext>(handler =>
                handler.UseScopedHandler<LegacyEmbeddedClientGuard>());
            options.AddEventHandler<OpenIddict.Server.OpenIddictServerEvents.ValidateTokenRequestContext>(handler =>
                handler.UseScopedHandler<LegacyEmbeddedClientGuard>());
        });
        if (!EmbeddedIdentityServerSettings.IsEnabled(configuration)) return builder;
        builder.Services.AddNegotiateOnce();
        builder.Services.PostConfigure<GlobalSettings>(global =>
            global.ReservedPaths = global.ReservedPaths.EnsureEndsWith(',') + "~/connect/,~/.well-known/");
        builder.Services.AddSingleton<EmbeddedIdentityServerHost>();
        builder.Services.Configure<UmbracoPipelineOptions>(options => options.AddFilter(new UmbracoPipelineFilter("IdentityServer.Sso.EmbeddedIdentityServer")
        {
            PrePipeline = app =>
            {
                // Construct at startup: invalid configuration or unreadable key files fail here.
                var server = app.ApplicationServices.GetRequiredService<EmbeddedIdentityServerHost>();
                app.Use((context, next) => EmbeddedIdentityServerDefaults.IsEmbeddedEndpoint(context.Request)
                    ? server.InvokeAsync(context) : next(context));
            },
        }));
        return builder;
    }
}
