namespace IdentityServer.Sso;

/// <summary>The first thing the backoffice login screen does.</summary>
public enum BackOfficeLoginFlow
{
    /// <summary>Shows the username and password form, with the provider buttons beside it.</summary>
    Form,
    /// <summary>Redirects to the identity server at once, and stops the site at startup if that provider is off.</summary>
    IdentityServer,
    // Value 2 belonged to the removed Windows flow; preserve the remaining numeric values.
    /// <summary>In debug mode, shows the form and never redirects automatically. Outside debug mode it acts as <see cref="Form"/>.</summary>
    Debug = 3,
    /// <summary>Redirects to the identity server when its backoffice provider is on and available, and shows the form otherwise.</summary>
    Auto,
}

/// <summary>Chooses the initial backoffice sign-in flow. Provider settings still control which SSO buttons are available.</summary>
public sealed class BackOfficeLoginSettings
{
    /// <summary>The configuration section these settings bind from.</summary>
    public const string SectionName = "BackOfficeLogin";

    /// <summary>The flow outside debug mode, and in debug mode when <see cref="DebugFlow"/> is null.</summary>
    public BackOfficeLoginFlow DefaultFlow { get; set; } = BackOfficeLoginFlow.Auto;

    /// <summary>Overrides DefaultFlow while Umbraco:CMS:Hosting:Debug is true. Null uses DefaultFlow.</summary>
    public BackOfficeLoginFlow? DebugFlow { get; set; } = BackOfficeLoginFlow.Debug;

    /// <summary>The flow that applies, given whether <c>Umbraco:CMS:Hosting:Debug</c> is on.</summary>
    public BackOfficeLoginFlow GetEffectiveFlow(bool debug)
    {
        var flow = debug ? DebugFlow ?? DefaultFlow : DefaultFlow;
        // Even an explicit Debug selection must never bypass passwords outside debug mode.
        return flow == BackOfficeLoginFlow.Debug && !debug ? BackOfficeLoginFlow.Form : flow;
    }

    /// <summary>The backoffice scheme the login screen redirects to automatically, or null for none. Throws when the flow names a provider that is switched off.</summary>
    public string? GetAutoRedirectScheme(bool debug, IdentityServerSettings identityServer)
        => GetEffectiveFlow(debug) switch
        {
            BackOfficeLoginFlow.Auto when identityServer.Enabled && identityServer.BackOfficeEnabled => identityServer.BackOfficeScheme,
            BackOfficeLoginFlow.IdentityServer when identityServer.Enabled && identityServer.BackOfficeEnabled => identityServer.BackOfficeScheme,
            BackOfficeLoginFlow.IdentityServer => throw new InvalidOperationException("BackOfficeLogin selects IdentityServer, but IdentityServer:Enabled and IdentityServer:BackOfficeEnabled must both be true."),
            _ => null,
        };
}
