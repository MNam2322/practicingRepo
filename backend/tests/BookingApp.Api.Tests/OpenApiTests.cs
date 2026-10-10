using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BookingApp.Api.Tests;

/// <summary>
/// The OpenAPI document is the contract with the frontend. It is committed as backend/openapi/v1.json so a change to it
/// shows up in review. Library: 8.2 "OpenAPI, versioning and contracts" ("commit the generated document").
/// After an intended contract change, regenerate it: UPDATE_OPENAPI=1 dotnet test
/// </summary>
public sealed class OpenApiTests : ApiTest
{
    private HttpClient DevelopmentClient() =>
        Factory.WithWebHostBuilder(b => b.UseEnvironment("Development")).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task The_document_matches_the_committed_contract()
    {
        using var client = DevelopmentClient();
        var actual = Normalise(await client.GetStringAsync("/openapi/v1.json", Ct));
        var path = Path.Combine(BackendDirectory(), "openapi", "v1.json");

        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, actual, Ct);
        }

        Assert.True(File.Exists(path), $"{path} is missing. Run: UPDATE_OPENAPI=1 dotnet test");
        var committed = (await File.ReadAllTextAsync(path, Ct)).ReplaceLineEndings("\n");
        Assert.True(committed == actual, "The API contract changed. If that was intended, run UPDATE_OPENAPI=1 dotnet test and commit backend/openapi/v1.json.");
    }

    [Fact]
    public async Task Every_operation_has_a_name_a_summary_and_its_errors()
    {
        using var client = DevelopmentClient();
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json", Ct));

        var operations = doc.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateObject().Select(o => (Route: $"{o.Name.ToUpperInvariant()} {p.Name}", Op: o.Value)))
            .ToList();
        Assert.Equal(35, operations.Count);
        Assert.All(operations, o =>
        {
            Assert.False(string.IsNullOrEmpty(o.Op.GetProperty("operationId").GetString()), $"{o.Route} has no name");
            Assert.False(string.IsNullOrEmpty(o.Op.GetProperty("summary").GetString()), $"{o.Route} has no summary");
        });

        var create = operations.Single(o => o.Route == "POST /api/bookings").Op.GetProperty("responses");
        Assert.Equal(["201", "400", "404", "409", "422", "429"], create.EnumerateObject().Select(r => r.Name));
        Assert.Contains("slot_taken", create.GetProperty("409").GetProperty("description").GetString());

        var problem = doc.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("ProblemDetails");
        Assert.True(problem.GetProperty("properties").TryGetProperty("code", out _));
    }

    [Fact]
    public async Task Scalar_serves_the_reference_page_in_development()
    {
        using var client = DevelopmentClient();
        var response = await client.GetAsync("/scalar/", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_document_and_scalar_are_not_served_outside_development()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/openapi/v1.json", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/scalar/", Ct)).StatusCode);
    }

    /// <summary>XML doc comments carry the platform's line endings; the committed file always uses \n.</summary>
    private static string Normalise(string json) => json.Replace("\\r\\n", "\\n", StringComparison.Ordinal).ReplaceLineEndings("\n").TrimEnd() + "\n";

    private static string BackendDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "BookingApp.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Could not find the backend folder (BookingApp.slnx) above " + AppContext.BaseDirectory);
    }
}
