using System.Reflection;
using System.Xml.Linq;
using BookingApp.Api.Controllers;
using BookingApp.Core.Bookings;
using BookingApp.Infrastructure;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace BookingApp.Api.Tests;

/// <summary>
/// The architecture as tests, so it cannot erode quietly (library 12.1 "Layers, and the dependency rule": "enforce it in a
/// test, or it erodes within a quarter"). backend/CLAUDE.md describes the rules; docs/adr/ says why.
/// </summary>
public sealed class ArchitectureTests : ApiTest
{
    private static readonly Assembly Core = typeof(Booking).Assembly;
    private static readonly Assembly Infrastructure = typeof(DependencyInjection).Assembly;
    private static readonly Assembly Api = typeof(ApiControllerBase).Assembly;

    /// <summary>backend/, found from the test's output folder.</summary>
    private static string BackendRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "BookingApp.slnx"))) return dir.FullName;
        throw new InvalidOperationException("backend/BookingApp.slnx not found above " + AppContext.BaseDirectory);
    }

    private static IEnumerable<XElement> References(string project) =>
        XDocument.Load(Path.Combine(BackendRoot(), "src", project, $"{project}.csproj")).Descendants()
            .Where(e => e.Name.LocalName is "PackageReference" or "ProjectReference" or "FrameworkReference" or "Reference");

    [Fact]
    public void Core_references_no_package_and_no_project()
    {
        // The mechanical test (library 12.1): any reference in Core's project file is a leak, used or not.
        Assert.Empty(References("BookingApp.Core").Select(e => $"{e.Name.LocalName} {e.Attribute("Include")?.Value}"));
    }

    [Fact]
    public void Core_depends_on_nothing_outside_the_base_class_library()
    {
        var result = Types.InAssembly(Core).ShouldNot().HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions", "Microsoft.Data",
                "Npgsql", "System.Net.Http", "BookingApp.Infrastructure", "BookingApp.Api")
            .GetResult();
        Assert.True(result.IsSuccessful, "Core types with outside dependencies: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Infrastructure_never_depends_on_the_api()
    {
        Assert.DoesNotContain(References("BookingApp.Infrastructure"), e => e.Attribute("Include")?.Value.Contains("BookingApp.Api", StringComparison.Ordinal) == true);
        var result = Types.InAssembly(Infrastructure).ShouldNot().HaveDependencyOn("BookingApp.Api").GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Controllers_never_touch_a_repository_or_the_database()
    {
        // Every storage port in Core (I*Repository, the outbox, the idempotency store) and every repository, and the
        // DbContext, in Infrastructure: controllers call services, services call these (backend/CLAUDE.md).
        string[] storage =
        [
            .. Core.GetTypes().Concat(Infrastructure.GetTypes())
                .Where(t => t.Name.EndsWith("Repository", StringComparison.Ordinal) || t.Name is "INotificationOutbox" or "IIdempotencyStore"
                    || typeof(Microsoft.EntityFrameworkCore.DbContext).IsAssignableFrom(t))
                .Select(t => t.FullName!),
            "Microsoft.EntityFrameworkCore",
            "BookingApp.Infrastructure.Repositories",
            "BookingApp.Infrastructure.Persistence",
        ];
        Assert.Contains("BookingApp.Core.Bookings.IBookingRepository", storage);   // the list is not empty by mistake

        var result = Types.InAssembly(Api).That().ResideInNamespace("BookingApp.Api.Controllers")
            .ShouldNot().HaveDependencyOnAny(storage).GetResult();
        Assert.True(result.IsSuccessful, "Controllers using storage directly: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Every_api_endpoint_is_a_controller_action()
    {
        // "Never use minimal APIs for application endpoints" (CLAUDE.md). Operational routes (health, the API reference,
        // the web app's fallback) may be minimal; nothing under /api may be.
        var endpoints = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        Assert.Contains(endpoints, e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is not null);

        var minimal = endpoints
            .Where(e => e.RoutePattern.RawText?.TrimStart('/').StartsWith("api", StringComparison.OrdinalIgnoreCase) == true)
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is null)
            .Where(e => e.RoutePattern.RawText!.TrimStart('/') != "api/{**path}")   // WebApp.cs: unknown /api paths answer 404
            .Select(e => e.DisplayName);
        Assert.Empty(minimal);
    }

    [Fact]
    public void Nothing_reads_the_clock_except_through_TimeProvider()
    {
        // Library 12.1, the last row of the table: ambient time makes code untestable; TimeProvider makes it free.
        string[] banned = ["get_Now", "get_UtcNow", "get_Today"];
        var offenders = new[] { Core, Infrastructure, Api }
            .SelectMany(a => ModuleDefinition.ReadModule(a.Location).Types.SelectMany(AllTypes))
            .SelectMany(t => t.Methods.Where(m => m.HasBody).Select(m => (Type: t, Method: m)))
            .Where(x => x.Method.Body.Instructions.Any(i =>
                (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && i.Operand is MethodReference called
                && called.DeclaringType.FullName is "System.DateTime" or "System.DateTimeOffset" && banned.Contains(called.Name)))
            .Select(x => $"{x.Type.FullName}.{x.Method.Name}")
            .ToList();
        Assert.Empty(offenders);

        static IEnumerable<TypeDefinition> AllTypes(TypeDefinition t) => t.NestedTypes.SelectMany(AllTypes).Prepend(t);
    }
}
