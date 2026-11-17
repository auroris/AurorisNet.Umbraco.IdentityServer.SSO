using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace IdentityServer.Sso.Tests;

public sealed class EmbeddedIdentityServerSettingsTests
{
    [Theory]
    [InlineData(null, null, null, false, false)]
    [InlineData(null, "true", "", false, true)]
    [InlineData(null, "true", "https://idp.example", false, false)]
    [InlineData(null, "false", "", false, false)]
    [InlineData(null, null, null, true, true)]
    [InlineData("false", "true", "", true, false)]
    [InlineData("true", null, null, false, true)]
    public void An_unset_Enabled_runs_the_server_only_where_something_uses_it(
        string? enabled, string? siteEnabled, string? authority, bool hasClient, bool expected)
    {
        var values = new Dictionary<string, string?>
        {
            ["EmbeddedIdentityServer:Enabled"] = enabled,
            ["IdentityServer:Enabled"] = siteEnabled,
            ["IdentityServer:Authority"] = authority,
            ["IdentityServer:ClientId"] = "site",
        };
        if (hasClient) values["EmbeddedIdentityServer:Clients:0:ClientId"] = "other-application";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values.Where(value => value.Value is not null)).Build();
        Assert.Equal(expected, EmbeddedIdentityServerSettings.IsEnabled(configuration));
    }

    [Fact]
    public void Settings_are_validated_before_the_site_starts()
    {
        var validator = new EmbeddedIdentityServerSettingsValidator(Options.Create(new IdentityServerSettings { Enabled = true, ClientId = "umbraco-backoffice" }));
        var result = validator.Validate(null, new EmbeddedIdentityServerSettings
        {
            Groups = ["(unclosed"],
            Clients =
            [
                new EmbeddedIdentityServerClient { ClientId = "app", RedirectUris = ["https://app.example/callback"] },
                new EmbeddedIdentityServerClient { ClientId = "APP", RedirectUris = ["/relative"] },
                new EmbeddedIdentityServerClient { ClientId = "umbraco-back-office", RedirectUris = ["https://app.example/#fragment"] },
                new EmbeddedIdentityServerClient { ClientId = "umbraco-backoffice" },
            ],
        });
        Assert.True(result.Failed);
        var failures = string.Join("\n", result.Failures!);
        Assert.Contains("(unclosed", failures);
        Assert.Contains("lists 'APP' more than once", failures);
        Assert.Contains("'/relative' must be an absolute", failures);
        Assert.Contains("belongs to Umbraco", failures);
        Assert.Contains("this site's own IdentityServer:ClientId", failures);
        Assert.Contains("needs at least one RedirectUris entry", failures);

        Assert.True(validator.Validate(null, new EmbeddedIdentityServerSettings
        {
            Clients = [new EmbeddedIdentityServerClient { ClientId = "app", RedirectUris = ["https://app.example/callback"] }],
        }).Succeeded);
    }

    [Fact]
    public void Legacy_key_and_directory_overrides_require_explicit_migration()
    {
        var validator = new EmbeddedIdentityServerSettingsValidator(Options.Create(new IdentityServerSettings()));
        var result = validator.Validate(null, new EmbeddedIdentityServerSettings
        {
            KeyPath = "old-signing.key", UseActiveDirectory = false,
        });
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("EmbeddedIdentityServer:DataPath"));
        Assert.Contains(result.Failures!, failure => failure.Contains("EmbeddedIdentityServer:UseActiveDirectory"));
    }

    [Fact]
    public void A_switched_off_server_is_not_validated()
        => Assert.True(new EmbeddedIdentityServerSettingsValidator(Options.Create(new IdentityServerSettings()))
            .Validate(null, new EmbeddedIdentityServerSettings { Enabled = false, Groups = ["(unclosed"] }).Succeeded);
}
