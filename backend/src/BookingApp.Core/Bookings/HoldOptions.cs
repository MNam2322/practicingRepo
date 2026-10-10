namespace BookingApp.Core.Bookings;

/// <summary>The "Holds" settings: how long a hold keeps a slot, and how often expired holds are swept.</summary>
public sealed class HoldOptions
{
    public const string Section = "Holds";

    /// <summary>How long a customer has to confirm (and pay) before the slot is released. Keep it longer than a payment takes.</summary>
    public double Minutes { get; set; } = 10;

    /// <summary>How often the expiry job looks for holds past their time.</summary>
    public int SweepSeconds { get; set; } = 15;

    public TimeSpan HoldFor => TimeSpan.FromMinutes(Minutes);
}
