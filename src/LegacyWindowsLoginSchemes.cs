namespace IdentityServer.Sso;

/// <summary>
/// Names used only to find stored links from the removed direct Windows provider.
/// These are not registered authentication schemes and new directory accounts use IdentityServer.
/// </summary>
public static class LegacyWindowsLoginSchemes
{
    /// <summary>The backoffice scheme of the removed Windows provider.</summary>
    public const string BackOffice = "Umbraco.Windows";
    /// <summary>The member scheme of the removed Windows provider.</summary>
    public const string Member = "UmbracoMembers.Windows";
}
