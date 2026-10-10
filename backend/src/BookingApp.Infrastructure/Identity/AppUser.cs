using Microsoft.AspNetCore.Identity;

namespace BookingApp.Infrastructure.Identity;

/// <summary>An ASP.NET Core Identity user. The Guid id doubles as the customer id on their bookings.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public const int MaxNameLength = 80;

    /// <summary>The name shown in the account menu and used as the default name on bookings.</summary>
    public string DisplayName { get; set; } = "";
}
