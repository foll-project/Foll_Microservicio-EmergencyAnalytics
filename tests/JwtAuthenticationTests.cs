using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using foll_emergency_analytics.EmergencyAnalytics.Domain.Services;
using foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Entities;
using foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Queries;
using foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Commands;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Foll.EmergencyAnalytics.Tests;

public class JwtAuthenticationTests
{
    private const string SigningKey = "test-only-signing-key-at-least-32-bytes";

    [Fact]
    public async Task StartsWithoutIamOrServiceCredentials()
    {
        using var app = new EmergencyApp();
        using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task PatientDeletionIsNoLongerExposedAsAnonymousHttp()
    {
        using var app = new EmergencyApp();
        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/internal/emergency/patients-deleted", new { patientIds = Array.Empty<long>() });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("wrong-signing-key-at-least-32-bytes", false)]
    [InlineData(SigningKey, true)]
    public async Task PublicApiRejectsMissingInvalidAndExpiredJwt(string? key, bool expired)
    {
        using var app = new EmergencyApp();
        using var client = app.CreateClient();
        if (key is not null) client.DefaultRequestHeaders.Authorization = new("Bearer", Token(key, expired));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/emergency/incidents/1")).StatusCode);
    }

    [Fact]
    public async Task ValidSignatureIsAcceptedLocallyWithoutIam()
    {
        using var app = new EmergencyApp();
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(SigningKey));
        var response = await client.GetAsync("/api/emergency/incidents/1");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ClosureActorNameComesFromValidatedJwtAndNotRequestBody()
    {
        var commands = new Commands();
        using var app = new EmergencyApp();
        using var configured = app.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IEmergencyIncidentCommandService>(commands)));
        using var client = configured.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(SigningKey));
        var response = await client.PostAsJsonAsync("/api/emergency/incidents/1/resolve", new { observation = "Atendido", closedByName = "Spoofed" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Ana Perez", commands.Last?.ActorName);
        Assert.Equal(7, commands.Last?.ActorUserId);
    }

    private static string Token(string key, bool expired = false) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            claims: [new Claim("userId", "7"), new Claim("firstName", "Ana"), new Claim("lastName", "Perez")], notBefore: DateTime.UtcNow.AddMinutes(-10),
            expires: DateTime.UtcNow.AddMinutes(expired ? -1 : 5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.ASCII.GetBytes(key)), SecurityAlgorithms.HmacSha256)));

    private sealed class EmergencyApp : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = SigningKey,
                ["EmergencyAnalyticsMqtt:Enabled"] = "false",
                ["Outbox:Enabled"] = "false",
                ["IntegrationConsumers:Enabled"] = "false",
                ["ApplyMigrationsOnStartup"] = "false",
                ["Services:Care:BaseUrl"] = "http://127.0.0.1:1"
            })).ConfigureTestServices(services => services.AddScoped<IEmergencyIncidentQueryService, NoIncidents>());
    }

    private sealed class NoIncidents : IEmergencyIncidentQueryService
    {
        public Task<EmergencyIncident?> Handle(GetEmergencyIncidentByIdQuery query) => Task.FromResult<EmergencyIncident?>(null);
        public Task<EmergencyIncident?> Handle(GetActiveFallIncidentByPatientIdQuery query) => Task.FromResult<EmergencyIncident?>(null);
        public Task<IReadOnlyCollection<EmergencyIncident>> Handle(ListFallIncidentHistoryByPatientIdQuery query) =>
            Task.FromResult<IReadOnlyCollection<EmergencyIncident>>([]);
    }

    private sealed class Commands : IEmergencyIncidentCommandService
    {
        public ResolveFallIncidentCommand? Last;
        public Task Handle(ResolveFallIncidentCommand command) { Last = command; return Task.CompletedTask; }
        public Task Handle(RegisterFallDetectedCommand command) => Task.CompletedTask;
        public Task Handle(RegisterFallCancelledCommand command) => Task.CompletedTask;
        public Task Handle(MarkFallIncidentFalsePositiveCommand command) => Task.CompletedTask;
        public Task Handle(UpdateEmergencyIncidentObservationCommand command) => Task.CompletedTask;
        public Task Handle(DeleteEmergenciesByAccountCommand command) => Task.CompletedTask;
    }
}
