using System.Net;
using System.Text;
using foll_backend.Shared.Infrastructure.Integration;
using foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Entities;
using foll_emergency_analytics.Shared.Infrastructure.Persistence.EFC.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Foll.EmergencyAnalytics.Tests;

public class ArchitectureAndTransitionTests
{
    [Fact]
    public void ModelOwnsOnlyEmergencyTablesAndHasDistinctOpenIndex()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(
            "Host=localhost;Database=not_used;Username=test;Password=test").UseSnakeCaseNamingConvention().Options);
        Assert.Equal(3, db.Model.GetEntityTypes().Count());
        Assert.All(db.Model.GetEntityTypes(), entity => Assert.Equal("emergency", entity.GetSchema()));
        var entity = db.Model.FindEntityType(typeof(EmergencyIncident))!;
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.GetFilter() == "\"status\" = 'Open'");
        Assert.Contains(entity.GetIndexes(), index => !index.IsUnique && index.Properties.SingleOrDefault()?.Name == "DeviceId");
        Assert.DoesNotContain(typeof(EmergencyIncident).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name == "foll-backend" || assembly.Name == "Foll-Care");
    }

    [Fact]
    public async Task ProxyPreservesResolveRequestAndUpstreamStatus()
    {
        using var client = DomainAndGatewayTests.Client(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/emergency/incidents/4/resolve?source=web", request.RequestUri!.PathAndQuery);
            Assert.Equal("Bearer client-jwt", request.Headers.Authorization?.ToString());
            Assert.Equal("{\"observation\":\"Atendido\"}", request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("business-error", Encoding.UTF8, "application/json") };
        });
        using var server = Server(true, client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/emergency/incidents/4/resolve?source=web");
        request.Headers.Authorization = new("Bearer", "client-jwt");
        request.Content = new StringContent("{\"observation\":\"Atendido\"}", Encoding.UTF8, "application/json");
        using var response = await server.GetTestClient().SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("business-error", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DisabledRemoteModeLeavesLocalRoutesActive()
    {
        using var client = DomainAndGatewayTests.Client(_ => throw new Exception("Should not call remote"));
        using var server = Server(false, client);
        Assert.Equal("local", await server.GetTestClient().GetStringAsync("/api/emergency/incidents/4"));
    }

    [Fact]
    public async Task RemoteFailureReturns502WithoutFallingBackToStaleLocalData()
    {
        using var client = DomainAndGatewayTests.Client(_ => throw new HttpRequestException("offline"));
        using var server = Server(true, client);
        using var response = await server.GetTestClient().GetAsync("/api/emergency/incidents/4");
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    private static IConfiguration Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    private static IHost Server(bool remote, HttpClient upstream) => new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
        .ConfigureServices(services =>
        {
            services.AddSingleton<IConfiguration>(Config(new() { ["EmergencyAnalytics:RemoteEnabled"] = remote.ToString() }));
            services.AddSingleton(new EmergencyServiceClient(upstream));
        })
        .Configure(app =>
        {
            app.UseMiddleware<EmergencyProxyMiddleware>();
            app.Run(context => context.Response.WriteAsync("local"));
        })).Start();
}
