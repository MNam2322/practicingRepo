namespace BookingApp.Infrastructure.Identity;

/// <summary>
/// The "Accounts:Bootstrap" settings: emails that get Staff or Admin. For setting up a club (and for development);
/// applied at start-up to existing accounts and whenever one of these accounts signs up or in.
/// </summary>
public sealed class AccountBootstrapOptions
{
    public const string Section = "Accounts:Bootstrap";

    public List<string> Staff { get; set; } = [];
    public List<string> Admin { get; set; } = [];

    /// <summary>The roles configured for this email.</summary>
    public IEnumerable<string> RolesFor(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) yield break;
        if (Staff.Contains(email, StringComparer.OrdinalIgnoreCase)) yield return Core.Accounts.Roles.Staff;
        if (Admin.Contains(email, StringComparer.OrdinalIgnoreCase)) yield return Core.Accounts.Roles.Admin;
    }
}
