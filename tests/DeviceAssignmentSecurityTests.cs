extern alias Devices;

using System.Net;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using foll_emergency_analytics.EmergencyAnalytics.Application.OutboundServices;
using foll_emergency_analytics.EmergencyAnalytics.Infrastructure.Gateways;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using DeviceAcl = Devices::foll_device_managment.DeviceManagment.Application.ACL;
using DeviceController = Devices::foll_device_managment.DeviceManagment.Interfaces.Internal.InternalDevicesController;

namespace Foll.EmergencyAnalytics.Tests;

public class DeviceAssignmentSecurityTests
{
    private const string Key = "synthetic-emergency-device-key-32-bytes-minimum";
    private const string SigningKey = "synthetic-user-jwt-signing-key-independent-from-api-key";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    public async Task AssignmentRejectsMissingAndIncorrectKey(string? key)
    {
        using var devices = DeviceHost(Key);
        using var client = devices.GetTestClient();
        if (!string.IsNullOrEmpty(key)) client.DefaultRequestHeaders.Add("X-Foll-Service-Key", key);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/internal/devices/1001/assignment")).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    public async Task UnconfiguredOrWeakReceiverKeyNeverAuthorizes(string? configured)
    {
        using var devices = DeviceHost(configured);
        using var client = devices.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Foll-Service-Key", configured ?? Key);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/internal/devices/1001/assignment")).StatusCode);
    }

    [Fact]
    public async Task KeyOnlyGrantsAssignmentAndDuplicateHeadersAreRejected()
    {
        using var devices = DeviceHost(Key);
        using var client = devices.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Foll-Service-Key", Key);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/internal/devices/1001/assignment")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/internal/devices/patient/25/status")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Foll-Service-Key", Key);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/internal/devices/1001/assignment")).StatusCode);
    }

    [Fact]
    public async Task RealEmergencyClientCanQueryDevicesWithoutUserJwtOrIam()
    {
        using var devices = DeviceHost(Key);
        using var emergency = new EmergencyApp(devices);
        using var scope = emergency.Services.CreateScope();
        var gateway = scope.ServiceProvider.GetRequiredService<IDeviceIncidentAssignmentService>();
        Assert.Equal(25, await gateway.FindAssignedPatientIdAsync(1001));
    }

    [Fact]
    public async Task UserJwtStillGrantsStatusButDoesNotReplaceAssignmentKey()
    {
        using var devices = DeviceHost(Key);
        using var client = devices.GetTestClient();
        var jwt = new JwtSecurityToken(claims: [new Claim("userId", "7")], expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.ASCII.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/internal/devices/patient/25/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/internal/devices/1001/assignment")).StatusCode);
    }

    private static IHost DeviceHost(string? key) => new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
        .ConfigureServices(services =>
        {
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InternalApi:EmergencyAssignmentKey"] = key
            }).Build());
            services.AddSingleton<DeviceAcl.IDeviceStatusAcl, StatusStub>();
            services.AddSingleton<DeviceAcl.IDeviceAssignmentAcl, AssignmentStub>();
            services.AddControllers().ConfigureApplicationPartManager(manager =>
            {
                manager.ApplicationParts.Clear();
                manager.ApplicationParts.Add(new AssemblyPart(typeof(DeviceController).Assembly));
            });
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(SigningKey)),
                ValidateIssuer = false, ValidateAudience = false, ValidateLifetime = true, ClockSkew = TimeSpan.Zero
            });
            services.AddAuthorization();
        }).Configure(app =>
        {
            app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
            app.UseEndpoints(endpoints => endpoints.MapControllers());
        })).Start();

    private sealed class EmergencyApp(IHost devices) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Services:Devices:ApiKey"] = Key,
                ["EmergencyAnalyticsMqtt:Enabled"] = "false",
                ["Outbox:Enabled"] = "false",
                ["IntegrationConsumers:Enabled"] = "false",
                ["ApplyMigrationsOnStartup"] = "false"
            })).ConfigureTestServices(services => services
                .AddHttpClient<IDeviceIncidentAssignmentService, DeviceIncidentAssignmentGateway>()
                .ConfigurePrimaryHttpMessageHandler(() => devices.GetTestServer().CreateHandler()));
    }

    private sealed class AssignmentStub : DeviceAcl.IDeviceAssignmentAcl
    {
        public Task<DeviceAcl.DeviceAssignmentDto?> GetAssignmentByDeviceIdAsync(long id) =>
            Task.FromResult<DeviceAcl.DeviceAssignmentDto?>(new(id, 25, "Active", "Connected"));
    }

    private sealed class StatusStub : DeviceAcl.IDeviceStatusAcl
    {
        public Task<DeviceAcl.DeviceStatusDto?> GetStatusByPatientIdAsync(long id) => Task.FromResult<DeviceAcl.DeviceStatusDto?>(null);
    }
}
