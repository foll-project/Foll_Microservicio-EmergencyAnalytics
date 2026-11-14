using System.Text;
using foll_emergency_analytics.EmergencyAnalytics.Application.Internal.CommandServices;
using foll_emergency_analytics.EmergencyAnalytics.Application.Internal.QueryServices;
using foll_emergency_analytics.EmergencyAnalytics.Application.OutboundServices;
using foll_emergency_analytics.EmergencyAnalytics.Application.Ports;
using foll_emergency_analytics.EmergencyAnalytics.Domain.Repositories;
using foll_emergency_analytics.EmergencyAnalytics.Domain.Services;
using foll_emergency_analytics.EmergencyAnalytics.Infrastructure.Configuration;
using foll_emergency_analytics.EmergencyAnalytics.Infrastructure.Gateways;
using foll_emergency_analytics.EmergencyAnalytics.Infrastructure.Messaging;
using foll_emergency_analytics.EmergencyAnalytics.Infrastructure.Persistence.EFC.Repositories;
using foll_emergency_analytics.Shared.Domain.Repositories;
using foll_emergency_analytics.Shared.Infrastructure.Configuration;
using foll_emergency_analytics.Shared.Infrastructure.Persistence.EFC.Configuration;
using foll_emergency_analytics.Shared.Infrastructure.Persistence.EFC.Repositories;
using foll_emergency_analytics.Shared.Infrastructure.Swagger;
using foll_emergency_analytics.Shared.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
var secret = builder.Configuration["JwtSettings:Secret"];
if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
    throw new InvalidOperationException("Configure JwtSettings:Secret con al menos 32 bytes, compatible con IAM.");

builder.Services.AddDbContext<AppDbContext>(options => options
    .UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", "emergency"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(secret)),
        ValidateIssuer = false, ValidateAudience = false, ValidateLifetime = true, ClockSkew = TimeSpan.Zero
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = context =>
        {
            if (context.Principal?.FindFirst("token_use")?.Value == "service")
                context.Fail("La API de usuarios requiere un JWT de usuario.");
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddServiceTokenClient(builder.Configuration, required: true);
builder.Services.AddServiceJwtValidation(builder.Configuration, new Dictionary<string, string>
{
    ["emergencies.location.read"] = "foll-emergency", ["emergencies.patients.delete"] = "foll-emergency"
});
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Foll EmergencyAnalytics v2", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header
    });
    options.OperationFilter<AuthorizeOperationFilter>();
});
builder.Services.Configure<EmergencyAnalyticsMqttOptions>(builder.Configuration.GetSection("EmergencyAnalyticsMqtt"));
builder.Services.AddOptions<OutboxOptions>().Bind(builder.Configuration.GetSection("Outbox"))
    .Validate(o => o.BatchSize > 0 && o.PollIntervalSeconds > 0, "Outbox requiere intervalos y lotes positivos.").ValidateOnStart();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IEmergencyIncidentRepository, EmergencyIncidentRepository>();
builder.Services.AddScoped<IFallTypeRepository, FallTypeRepository>();
builder.Services.AddScoped<IEmergencyOutboxMessageRepository, EmergencyOutboxMessageRepository>();
builder.Services.AddScoped<IEmergencyIncidentCommandService, EmergencyIncidentCommandService>();
builder.Services.AddScoped<IEmergencyIncidentQueryService, EmergencyIncidentQueryService>();
builder.Services.AddHttpClient<IDeviceIncidentAssignmentService, DeviceIncidentAssignmentGateway>(client => ConfigureClient(client, "Devices"))
    .AddHttpMessageHandler(sp => new ServiceJwtHandler(sp.GetRequiredService<IServiceTokenProvider>(), "foll-devices"));
builder.Services.AddHttpClient<IPatientIncidentAccessService, PatientIncidentAccessGateway>(client => ConfigureClient(client, "Care"));
builder.Services.AddHttpClient<IEmergencyEventGateway, EmergencyEventGateway>(client => ConfigureClient(client, "Notifications"))
    .AddHttpMessageHandler(sp => new ServiceJwtHandler(sp.GetRequiredService<IServiceTokenProvider>(), "foll-notifications"));
builder.Services.AddSingleton<IEmergencyAnalyticsMqttPublisher, EmergencyAnalyticsMqttPublisher>();
if (builder.Configuration.GetValue("EmergencyAnalyticsMqtt:Enabled", true))
    builder.Services.AddHostedService<EmergencyAnalyticsMqttSubscriberBackgroundService>();
if (builder.Configuration.GetValue("Outbox:Enabled", true))
    builder.Services.AddHostedService<EmergencyOutboxPublisherBackgroundService>();

var app = builder.Build();
if (builder.Configuration.GetValue("ApplyMigrationsOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}
if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger(); app.UseSwaggerUI();
}
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health/live", () => Results.Ok(new { service = "EmergencyAnalytics", status = "alive" }));
app.MapGet("/health/ready", async (AppDbContext db, CancellationToken ct) =>
{
    try
    {
        await db.FallTypes.AsNoTracking().AnyAsync(ct);
        await db.EmergencyOutboxMessages.AsNoTracking().AnyAsync(ct);
        await db.EmergencyIncidents.AsNoTracking().AnyAsync(ct);
        return Results.Ok(new { status = "ready" });
    }
    catch { return Results.StatusCode(503); }
});
app.MapControllers();
app.Run();

void ConfigureClient(HttpClient client, string name)
{
    var url = builder.Configuration[$"Services:{name}:BaseUrl"]
        ?? throw new InvalidOperationException($"Falta Services:{name}:BaseUrl.");
    client.BaseAddress = new Uri(url.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue($"Services:{name}:TimeoutSeconds", 10));
}

public partial class Program;
