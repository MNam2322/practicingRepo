namespace BookingApp.Core.Accounts;

/// <summary>A signed-up person. Their id is also their <see cref="Bookings.CustomerId"/>.</summary>
public sealed record Account(Guid Id, string Email, string Name, IReadOnlyList<string> Roles)
{
    public Bookings.CustomerId CustomerId => new(Id);
}

/// <summary>The roles. Code checks capabilities (policies), and roles map to them in one place (Api, Authorization).</summary>
public static class Roles
{
    public const string Customer = "Customer";
    public const string Staff = "Staff";
    public const string Admin = "Admin";

    public static readonly IReadOnlyList<string> All = [Customer, Staff, Admin];
}
