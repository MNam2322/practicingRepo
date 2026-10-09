using System.Net;
using System.Net.Http.Json;
using BookingApp.Core.Bookings;
using BookingApp.Infrastructure.Persistence;

namespace BookingApp.Api.Tests;

/// <summary>Bookings live in the SQLite file, so they survive the app stopping and starting again.</summary>
public sealed class SqlitePersistenceTests : IDisposable
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase) },
    };

    private readonly string _file = Path.Combine(Path.GetTempPath(), $"booking-restart-{Guid.NewGuid():N}.db");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_booking_survives_a_restart_and_still_blocks_its_slot()
    {
        var cookie = $"{CurrentCustomer.CookieName}={CustomerId.New()}";
        BookingResponse created;
        using (var first = new ApiFactory(StorageProvider.Sqlite, _file))
        {
            using var client = first.CreateClient();
            client.DefaultRequestHeaders.Add("Cookie", cookie);
            var response = await client.PostAsJsonAsync("/api/bookings",
                new { resourceId = "court-1", start = ApiFactory.At(ApiFactory.Tomorrow, 10), slots = 2, name = "Nam" }, Ct);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            created = (await response.Content.ReadFromJsonAsync<BookingResponse>(Json, Ct))!;
        }

        using var second = new ApiFactory(StorageProvider.Sqlite, _file);
        using var again = second.CreateClient();
        again.DefaultRequestHeaders.Add("Cookie", cookie);
        var mine = await again.GetFromJsonAsync<List<BookingResponse>>("/api/bookings/mine", Json, Ct);
        Assert.Equal(created, Assert.Single(mine!));

        var clash = await again.PostAsJsonAsync("/api/bookings",
            new { resourceId = "court-1", start = ApiFactory.At(ApiFactory.Tomorrow, 11), slots = 1, name = "Other" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
    }

    public void Dispose() => ApiFactory.DeleteDatabase(_file);
}
