using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Security;

namespace IdentityServer.Sso;

/// <summary>Gives a newly provisioned member a private random password until an administrator resets it.</summary>
public static class ProvisionedMemberPassword
{
    /// <summary>Sets a generated password on <paramref name="member"/> unless it already has one.</summary>
    public static async Task InitializeAsync(IMember member, IMemberManager manager)
    {
        // A username lookup caches an uncommitted member even if creation is rolled back.
        var identity = await manager.FindByIdAsync(member.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            ?? throw new InvalidOperationException($"The new member {member.Username} could not be loaded.");
        // Preserve a password supplied by a site's member-creation notification handler.
        if (!string.IsNullOrEmpty(identity.PasswordHash)) return;
        var result = await manager.AddPasswordAsync(identity, manager.GeneratePassword());
        if (!result.Succeeded)
            throw new InvalidOperationException($"The password for member {member.Username} could not be initialized: "
                + string.Join("; ", result.Errors.Select(error => error.Description)));
    }
}
