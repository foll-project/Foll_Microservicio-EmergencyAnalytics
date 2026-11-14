using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using foll_backend.IAM.Interfaces.REST;
using foll_emergency_analytics.Shared.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Xunit;
namespace Foll.EmergencyAnalytics.Tests;

public class ServiceJwtTests
{
    private const string SigningKey = "test-only-signing-key-at-least-32-bytes";
    private const string ClientSecret = "test-only-client-secret-at-least-32-characters";
    [Fact]
    public async Task IamIssuesShortLivedServiceTokenWithServerControlledScopes()
    {
        using var host = Host(); using var client = host.GetTestClient();
        var token = await Obtain(client, "notification-communication", "foll-emergency");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("foll-iam", jwt.Issuer); Assert.Contains("foll-emergency", jwt.Audiences);
        Assert.Equal("service", jwt.Claims.Single(c => c.Type == "token_use").Value);
        Assert.Equal("emergencies.location.read", jwt.Claims.Single(c => c.Type == "scope").Value);
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "userId"); Assert.InRange(jwt.ValidTo - DateTime.UtcNow, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(6));
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/probe/location")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/probe/delete")).StatusCode);
    }
    [Fact]
    public async Task BackendCanDeleteButEmergencyCannotRequestThatGrant()
    {
        using var host = Host(); using var client = host.GetTestClient();
        var token = await Obtain(client, "foll-backend", "foll-emergency");
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/probe/delete")).StatusCode);
        using var form = Form("emergency-analytics", "foll-emergency");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/iam/service-tokens", form)).StatusCode);
    }
    [Theory]
    [InlineData("unknown", ClientSecret)]
    [InlineData("emergency-analytics", "wrong")]
    public async Task InvalidClientCannotObtainTokens(string id, string secret)
    {
        using var host = Host(); using var client = host.GetTestClient(); using var form = Form(id, "foll-devices", secret);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/iam/service-tokens", form)).StatusCode);
    }
    [Fact]
    public async Task LegacyHeaderAndUserTokenCannotAuthorizeInternalEmergency()
    {
        using var host = Host(); using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Foll-Service-Key", ClientSecret);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/probe/location")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(use: "user"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/probe/location")).StatusCode);
    }
    [Theory]
    [InlineData("other-issuer", "foll-emergency", false)]
    [InlineData("foll-iam", "foll-devices", false)]
    [InlineData("foll-iam", "foll-emergency", true)]
    public async Task IssuerAudienceAndExpirationAreValidated(string issuer, string audience, bool expired)
    {
        using var host = Host(); using var client = host.GetTestClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", Token(issuer, audience, expired));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/probe/location")).StatusCode);
    }
    [Fact]
    public async Task IncorrectSignatureIsRejected()
    {
        using var host = Host(); using var client = host.GetTestClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", Token(key: "other-signing-key-with-at-least-32-bytes"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/probe/location")).StatusCode);
    }
    [Fact]
    public async Task ServiceTokenIsCachedByAudienceAndRenewedBeforeExpiration()
    {
        var requests = 0;
        using var client = DomainAndGatewayTests.Client(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { access_token = "service-token-" + ++requests, token_type = "Bearer", expires_in = 300 })
        });
        var clock = new Clock(); using var provider = new ServiceTokenProvider(new Factory(client), ClientConfig(), clock);
        Assert.Equal("service-token-1", await provider.GetAsync("foll-devices"));
        Assert.Equal("service-token-1", await provider.GetAsync("foll-devices"));
        Assert.Equal("service-token-2", await provider.GetAsync("foll-notifications"));
        clock.Now = clock.Now.AddSeconds(271);
        Assert.Equal("service-token-3", await provider.GetAsync("foll-devices")); Assert.Equal(3, requests);
    }
    [Fact]
    public async Task IssuerFailureDoesNotFallBackToSharedHeader()
    {
        using var client = DomainAndGatewayTests.Client(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var provider = new ServiceTokenProvider(new Factory(client), ClientConfig(), TimeProvider.System);
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetAsync("foll-devices"));
    }
    [Fact]
    public async Task OutgoingWorkerCallUsesBearerOnlyAndProxyPreservesUserToken()
    {
        var tokens = new Tokens();
        using var handler = new ServiceJwtHandler(tokens, "foll-devices") { InnerHandler = new Inspect(request =>
        {
            Assert.False(request.Headers.Contains("X-Foll-Service-Key"));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }) };
        using var client = new HttpClient(handler);
        await client.GetAsync("http://devices/internal/devices/1001/assignment"); Assert.Equal(1, tokens.Calls);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://devices/api/devices/1001/status"); request.Headers.Authorization = new("Bearer", "user-jwt");
        await client.SendAsync(request); Assert.Equal(1, tokens.Calls);
    }
    private static IConfiguration ClientConfig() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
    {
        ["ServiceAuthentication:TokenUrl"] = "http://iam/api/iam/service-tokens", ["ServiceAuthentication:ClientId"] = "emergency-analytics", ["ServiceAuthentication:ClientSecret"] = ClientSecret
    }).Build();
    private static FormUrlEncodedContent Form(string id, string audience, string secret = ClientSecret) => new(new Dictionary<string,string>
    { ["grant_type"] = "client_credentials", ["client_id"] = id, ["client_secret"] = secret, ["audience"] = audience });
    private static async Task<string> Obtain(HttpClient client, string id, string audience)
    {
        using var form = Form(id, audience); using var response = await client.PostAsync("/api/iam/service-tokens", form); response.EnsureSuccessStatusCode();
        var value = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(); return value.GetProperty("access_token").GetString()!;
    }
    private static string Token(string issuer = "foll-iam", string audience = "foll-emergency", bool expired = false, string use = "service", string key = SigningKey) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(issuer, audience,
            [new Claim("token_use", use), new Claim("client_id", "notification-communication"), new Claim("scope", "emergencies.location.read")],
            DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddMinutes(expired ? -1 : 5),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.ASCII.GetBytes(key)), SecurityAlgorithms.HmacSha256)));
    private static IHost Host() => new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureServices(s =>
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["JwtSettings:Secret"] = SigningKey, ["ServiceClients:EmergencyAnalytics:Secret"] = ClientSecret,
            ["ServiceClients:Notifications:Secret"] = ClientSecret, ["ServiceClients:Backend:Secret"] = ClientSecret
        }).Build();
        s.AddSingleton<IConfiguration>(config); s.AddControllers().ConfigureApplicationPartManager(m => { m.ApplicationParts.Clear(); m.ApplicationParts.Add(new AssemblyPart(typeof(ServiceTokensController).Assembly)); });
        s.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = false, ValidateAudience = false, IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(SigningKey)) });
        s.AddServiceJwtValidation(config, new Dictionary<string,string> { ["emergencies.location.read"] = "foll-emergency", ["emergencies.patients.delete"] = "foll-emergency" });
    }).Configure(app =>
    {
        app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseEndpoints(e =>
        {
            e.MapControllers(); e.MapGet("/probe/location", () => "ok").RequireAuthorization("service:emergencies.location.read");
            e.MapGet("/probe/delete", () => "ok").RequireAuthorization("service:emergencies.patients.delete");
        });
    })).Start();
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class Tokens : IServiceTokenProvider { public int Calls; public Task<string> GetAsync(string audience, CancellationToken ct = default) { Calls++; return Task.FromResult("service-jwt"); } }
    private sealed class Inspect(Func<HttpRequestMessage,HttpResponseMessage> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme); Assert.Contains(request.Headers.Authorization?.Parameter, new[] { "service-jwt", "user-jwt" }); return Task.FromResult(action(request));
        }
    }
}
