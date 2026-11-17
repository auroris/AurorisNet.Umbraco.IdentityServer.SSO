namespace IdentityServer.Sso;

/// <summary>Configuration for Umbraco's native backoffice external-login options.</summary>
public sealed class BackOfficeAutoLinkSettings
{
    /// <summary>Creates a backoffice user at the first sign-in of a person who has none. Off links only to existing users.</summary>
    public bool AutoLinkExternalAccount { get; set; }

    /// <summary>User group aliases for newly created users. Empty grants no backoffice groups.</summary>
    public string[] DefaultUserGroups { get; set; } = [];

    /// <summary>The culture of newly created users; null uses Umbraco's default.</summary>
    public string? DefaultCulture { get; set; }

    /// <summary>Lets a signed-in user link the provider to their account from their profile.</summary>
    public bool AllowManualLinking { get; set; } = true;
}

/// <summary>Configuration for Umbraco's native member external-login options.</summary>
public sealed class MemberAutoLinkSettings
{
    /// <summary>Creates a member at the first sign-in of a person who has none.</summary>
    public bool AutoLinkExternalAccount { get; set; } = true;

    /// <summary>Approves newly created members, so they can sign in at once.</summary>
    public bool DefaultIsApproved { get; set; } = true;

    /// <summary>The member type alias of newly created members.</summary>
    public string DefaultMemberTypeAlias { get; set; } = "Member";

    /// <summary>The culture of newly created members; null uses Umbraco's default.</summary>
    public string? DefaultCulture { get; set; }

    /// <summary>Member group names that newly created members join.</summary>
    public string[] DefaultMemberGroups { get; set; } = [];

    /// <summary>Creates members that can only sign in through the provider, without a usable password.</summary>
    public bool ExternalOnly { get; set; }
}
