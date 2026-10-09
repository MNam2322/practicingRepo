using BookingApp.Core.Scheduling;

namespace BookingApp.Core.Tests;

/// <summary>Shared fixtures: a venue in Ho Chi Minh City (UTC+7, no DST) and a clock fixed on a Monday morning.</summary>
internal static class TestData
{
    public static readonly Venue Club = new("Test club", "Asia/Ho_Chi_Minh");

    /// <summary>Monday 1 June 2026, 07:00 at the venue.</summary>
    public static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Tuesday 2 June 2026, tomorrow at the venue.</summary>
    public static readonly DateOnly Tomorrow = new(2026, 6, 2);

    public static Resource Court(string id = "court-1", int slotMinutes = 60, int maxSlots = 2) =>
        new(id, $"Court {id}", ResourceKind.Court, 4, slotMinutes, maxSlots, EveryDay(new TimeOnly(6, 0), new TimeOnly(22, 0)));

    public static IReadOnlyList<OpeningHours> EveryDay(TimeOnly open, TimeOnly close) =>
        [.. Enum.GetValues<DayOfWeek>().Select(d => new OpeningHours(d, open, close))];

    /// <summary>A venue-local wall-clock time on a day, as a UTC instant.</summary>
    public static DateTimeOffset At(DateOnly day, int hour, int minute = 0, Venue? venue = null) =>
        AvailabilityCalculator.ToUtc(day.ToDateTime(new TimeOnly(hour, minute)), venue ?? Club);

    public static TimeRange Hour(DateOnly day, int hour) => TimeRange.FromStart(At(day, hour), TimeSpan.FromHours(1));
}

internal sealed class TestResources(params Resource[] resources) : IResourceRepository
{
    private readonly List<Closure> _closures = [];

    public Task<ResourceCatalog> GetCatalogAsync(CancellationToken cancellationToken) => Task.FromResult(new ResourceCatalog(TestData.Club, resources));

    public Task<IReadOnlyList<Closure>> ClosuresAsync(string resourceId, TimeRange window, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Closure>>([.. _closures.Where(c => c.AppliesTo(resourceId) && c.Time.Overlaps(window)).OrderBy(c => c.Time.Start)]);

    public Task AddClosureAsync(Closure closure, CancellationToken cancellationToken)
    {
        _closures.Add(closure);
        return Task.CompletedTask;
    }
}
