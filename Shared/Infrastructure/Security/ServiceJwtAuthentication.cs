using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace foll_emergency_analytics.Shared.Infrastructure.Security;

public interface IServiceTokenProvider
{
    Task<string> GetAsync(string audience, CancellationToken ct = default);
}

public sealed class ServiceTokenProvider(IHttpClientFactory clients, IConfiguration configuration, TimeProvider clock) : IServiceTokenProvider, IDisposable
{
    private readonly ConcurrentDictionary<string, (string Token, DateTimeOffset RefreshAt)> _cache = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<string> GetAsync(string audience, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(audience, out var entry) && entry.RefreshAt > clock.GetUtcNow()) return entry.Token;
        await _gate.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(audience, out entry) && entry.RefreshAt > clock.GetUtcNow()) return entry.Token;
            var settings = configuration.GetSection("ServiceAuthentication");
            var url = settings["TokenUrl"];
            var id = settings["ClientId"];
            var secret = settings["ClientSecret"];
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
                string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret))
                throw new InvalidOperationException("Configure ServiceAuthentication:TokenUrl, ClientId y ClientSecret para obtener JWT de servicio.");
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials", ["client_id"] = id, ["client_secret"] = secret, ["audience"] = audience
            });
            using var response = await clients.CreateClient("ServiceTokenIssuer").PostAsync(uri, form, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
            if (result is null || string.IsNullOrWhiteSpace(result.AccessToken) ||
                !string.Equals(result.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase) || result.ExpiresIn <= 0)
                throw new InvalidOperationException("IAM no devolvió un access_token Bearer válido.");
            _cache[audience] = (result.AccessToken, clock.GetUtcNow().AddSeconds(result.ExpiresIn - Math.Min(30, result.ExpiresIn / 2d)));
            return result.AccessToken;
        }
        finally { _gate.Release(); }
    }
    public void Dispose() => _gate.Dispose();
    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("token_type")] string TokenType, [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

public sealed class ServiceJwtHandler(IServiceTokenProvider tokens, string audience) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Headers.Authorization is null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAsync(audience, ct));
        return await base.SendAsync(request, ct);
    }
}

public static class ServiceJwtAuthentication
{
    public const string Scheme = "ServiceBearer";
    public static IServiceCollection AddServiceTokenClient(this IServiceCollection services, IConfiguration config, bool required = false)
    {
        if (required && (string.IsNullOrWhiteSpace(config["ServiceAuthentication:TokenUrl"]) ||
            string.IsNullOrWhiteSpace(config["ServiceAuthentication:ClientId"]) ||
            string.IsNullOrWhiteSpace(config["ServiceAuthentication:ClientSecret"])))
            throw new InvalidOperationException("Configure ServiceAuthentication:TokenUrl, ClientId y ClientSecret.");
        services.AddHttpClient("ServiceTokenIssuer", c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<IServiceTokenProvider, ServiceTokenProvider>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
    public static IServiceCollection AddServiceJwtValidation(this IServiceCollection services, IConfiguration config,
        IReadOnlyDictionary<string, string> scopes)
    {
        var secret = config["JwtSettings:Secret"];
        if (string.IsNullOrWhiteSpace(secret)) throw new InvalidOperationException("Falta JwtSettings:Secret.");
        services.AddAuthentication().AddJwtBearer(Scheme, options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(secret)),
                ValidateIssuer = true, ValidIssuer = config["ServiceJwt:Issuer"] ?? "foll-iam",
                ValidateAudience = true, ValidAudiences = scopes.Values.Distinct(), ValidateLifetime = true,
                RequireExpirationTime = true, RequireSignedTokens = true, ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ClockSkew = TimeSpan.FromSeconds(5)
            };
        });
        services.AddAuthorization(options =>
        {
            foreach (var (scope, audience) in scopes)
                options.AddPolicy("service:" + scope, policy => policy.AddAuthenticationSchemes(Scheme)
                    .RequireAuthenticatedUser().RequireClaim("token_use", "service").RequireClaim("client_id")
                    .RequireClaim("aud", audience).RequireAssertion(ctx => ctx.User.FindAll("scope")
                        .Any(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope, StringComparer.Ordinal))));
        });
        return services;
    }
}
