using BookingApp.Core.Scheduling;
using Microsoft.Extensions.Options;

namespace BookingApp.Infrastructure.Catalog;

/// <summary>Shape of the "Venue" configuration section (appsettings.json).</summary>
public sealed class VenueOptions
{
    public const string Section = "Venue";

    public string Name { get; set; } = "";
    public string TimeZoneId { get; set; } = "UTC";
    public List<ResourceOptions> Resources { get; set; } = [];

    public sealed class ResourceOptions
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public ResourceKind Kind { get; set; } = ResourceKind.Other;
        public int Capacity { get; set; } = 1;
        public int SlotMinutes { get; set; } = 60;
        public int MaxSlotsPerBooking { get; set; } = 2;
        /// <summary>"Monday-Friday": "08:00-20:00" style entries. Days not listed are closed.</summary>
        public Dictionary<string, string> Hours { get; set; } = [];
    }
}

/// <summary>Builds the venue and resources from configuration once, at startup, and validates them.</summary>
internal sealed class ConfigurationResourceCatalog : IResourceCatalog
{
    private readonly Dictionary<string, Resource> _byId;

    public ConfigurationResourceCatalog(IOptions<VenueOptions> options)
    {
        var o = options.Value;
        Venue = new Venue(o.Name, o.TimeZoneId);
        Resources = [.. o.Resources.Select(ToResource)];
        _byId = Resources.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
    }

    public Venue Venue { get; }
    public IReadOnlyList<Resource> Resources { get; }
    public Resource? Find(string id) => _byId.GetValueOrDefault(id);

    private static Resource ToResource(VenueOptions.ResourceOptions r)
    {
        var hours = new List<OpeningHours>();
        foreach (var (days, span) in r.Hours)
        {
            var times = span.Split('-', StringSplitOptions.TrimEntries);
            var open = TimeOnly.Parse(times[0], System.Globalization.CultureInfo.InvariantCulture);
            var close = TimeOnly.Parse(times[1], System.Globalization.CultureInfo.InvariantCulture);
            if (close <= open) throw new InvalidOperationException($"Resource '{r.Id}': closing time must be after opening time in '{span}'.");
            if ((close - open).TotalMinutes % r.SlotMinutes != 0)
                throw new InvalidOperationException($"Resource '{r.Id}': opening hours '{span}' are not a whole number of {r.SlotMinutes}-minute slots.");
            foreach (var day in ExpandDays(days)) hours.Add(new OpeningHours(day, open, close));
        }
        return new Resource(r.Id, r.Name, r.Kind, r.Capacity, r.SlotMinutes, r.MaxSlotsPerBooking, hours);
    }

    /// <summary>"Monday-Friday", "Saturday", "Sunday" and similar.</summary>
    private static IEnumerable<DayOfWeek> ExpandDays(string days)
    {
        var parts = days.Split('-', StringSplitOptions.TrimEntries);
        var first = Enum.Parse<DayOfWeek>(parts[0], ignoreCase: true);
        var last = parts.Length > 1 ? Enum.Parse<DayOfWeek>(parts[1], ignoreCase: true) : first;
        for (var d = first; ; d = (DayOfWeek)(((int)d + 1) % 7))
        {
            yield return d;
            if (d == last) break;
        }
    }
}
