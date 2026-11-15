extern alias Emergency;
extern alias Notifications;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using EmergencyDb = Emergency::foll_emergency_analytics.Shared.Infrastructure.Persistence.EFC.Configuration.AppDbContext;
using NotificationDb = Notifications::foll_notifications.Shared.Infrastructure.Persistence.EFC.Configuration.AppDbContext;
using E = Emergency::foll_emergency_analytics.Shared.Infrastructure.Messaging;
using N = Notifications::foll_notifications.NotificationCommunication.Infrastructure.Messaging;
using NC = Notifications::foll_notifications.NotificationCommunication.Application.Contracts;
using Sender = Emergency::foll_emergency_analytics.EmergencyAnalytics.Infrastructure.Messaging.RabbitMqEmergencyEventPublisher;
using Envelope = Emergency::foll_emergency_analytics.EmergencyAnalytics.Application.Ports.EmergencyEventEnvelope;
using Incident = Emergency::foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Entities.EmergencyIncident;
using Outbox = Emergency::foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Entities.EmergencyOutboxMessage;

var config = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json").Build();
var connection = new NpgsqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection"));
const string testPrefix = "foll_checks_emerg_notify_";
var testDatabase = testPrefix + Guid.NewGuid().ToString("N");
var adminSettings = new NpgsqlConnectionStringBuilder(connection.ConnectionString) { Database = "postgres", Pooling = false };
var testSettings = new NpgsqlConnectionStringBuilder(connection.ConnectionString) { Database = testDatabase, Pooling = false };
await using var admin = new NpgsqlConnection(adminSettings.ConnectionString);
await admin.OpenAsync();
await using (var create = new NpgsqlCommand($"CREATE DATABASE {testDatabase}", admin)) await create.ExecuteNonQueryAsync();
var checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAIL: " + description);
    checks++; Console.WriteLine("PASS: " + description);
}
try
{
    var emergencyOptions = new DbContextOptionsBuilder<EmergencyDb>().UseNpgsql(testSettings.ConnectionString,
        pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", "emergency")).UseSnakeCaseNamingConvention().Options;
    var notificationOptions = new DbContextOptionsBuilder<NotificationDb>().UseNpgsql(testSettings.ConnectionString,
        pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", "notification")).UseSnakeCaseNamingConvention().Options;
    await using var emergency = new EmergencyDb(emergencyOptions);
    await using var notifications = new NotificationDb(notificationOptions);
    await emergency.Database.MigrateAsync();
    await notifications.Database.MigrateAsync();
    var incident = Incident.CreateFallDetected(1001, 25, 1, DateTime.UtcNow, .9m, -12, -77, "Lima", "{}");
    emergency.EmergencyIncidents.Add(incident);
    var opening = new NC.EmergencyIncidentOpenedIntegrationEvent(incident.IncidentKey, 1001, 25, 1, "FRONTAL", "Caida", 1,
        DateTime.UtcNow, .9m, -12, -77, "Lima");
    var pending = Outbox.Create(NC.EmergencyAnalyticsEventTypes.IncidentOpenedV1, opening, DateTime.UtcNow);
    emergency.EmergencyOutboxMessages.Add(pending);
    await emergency.SaveChangesAsync();
    var payload = JsonSerializer.Deserialize<JsonElement>(pending.Payload);
    var broker = Sender.CreateMessage(new Envelope($"{pending.Type}:{incident.IncidentKey:D}", pending.Type, payload, pending.OccurredOn));
    var body = JsonSerializer.SerializeToUtf8Bytes(broker);
    var received = Notifications::foll_notifications.Shared.Infrastructure.Messaging.BrokerEnvelope.Parse(broker.EventType,
        broker.EventId.ToString(), broker.OperationId.ToString(), broker.EventType, body);
    var inbox = new N.NotificationInbox(notifications);
    var handler = new Notifications::foll_notifications.Shared.Infrastructure.Messaging.NotificationEventHandler(inbox, new N.TelemetryBuffer(config));
    await handler.HandleAsync(received, default);
    await handler.HandleAsync(received, default);
    Check(await notifications.InboxMessages.CountAsync() == 1, "Emergency event is durably accepted once across repeated deliveries");
    var accepted = await notifications.InboxMessages.SingleAsync();
    Check(accepted.ProcessedAt is null && accepted.StreamKey == "patient:25", "ACK acceptance remains separate from user delivery");
    var changed = payload.Deserialize<NC.EmergencyIncidentOpenedIntegrationEvent>()! with { Address = "Changed" };
    var conflict = false;
    try { await inbox.AcceptAsync(new NC.IntegrationEnvelope(accepted.EventId, accepted.Type, JsonSerializer.SerializeToElement(changed)), default); }
    catch (InvalidOperationException) { conflict = true; }
    Check(conflict && await notifications.InboxMessages.CountAsync() == 1, "reused event identity with changed content is rejected");

    var completion = new E.AccountDeletionCompleted(Guid.NewGuid(), Guid.NewGuid(), 7, [25], DateTime.UtcNow);
    var completionEnvelope = E.BrokerEnvelope.Parse(E.AccountDeletionCompleted.EventType, completion.EventId.ToString(),
        completion.OperationId.ToString(), E.AccountDeletionCompleted.EventType, JsonSerializer.SerializeToUtf8Bytes(completion));
    var processor = new E.InboxProcessor(emergency, new E.PatientsDeletedHandler(emergency));
    Check(await processor.ProcessAsync(completionEnvelope, default), "Emergency consumes Care patient cleanup event");
    Check(!await emergency.EmergencyIncidents.AsNoTracking().AnyAsync() && !await emergency.EmergencyOutboxMessages.AsNoTracking().AnyAsync(),
        "cleanup deletes incidents and pending outbox in the same transaction");
    Check(!await processor.ProcessAsync(completionEnvelope, default), "duplicate cleanup is skipped by the transactional inbox");
    var invalid = completion with { EventId = Guid.NewGuid(), DeletedPatientIds = [-1] };
    var invalidEnvelope = E.BrokerEnvelope.Parse(E.AccountDeletionCompleted.EventType, invalid.EventId.ToString(), invalid.OperationId.ToString(),
        E.AccountDeletionCompleted.EventType, JsonSerializer.SerializeToUtf8Bytes(invalid));
    var rejected = false;
    try { await processor.ProcessAsync(invalidEnvelope, default); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected && await emergency.Set<E.IntegrationInboxMessage>().CountAsync() == 1, "invalid cleanup is rejected without committing an inbox receipt");
    if (args.Contains("--rabbitmq")) await RabbitMqChecks.RunAsync(config, emergencyOptions, notificationOptions, Check);
    Console.WriteLine($"{checks} integration checks passed. RabbitMQ: {(args.Contains("--rabbitmq") ? "enabled" : "disabled")}.");
}
finally
{
    NpgsqlConnection.ClearAllPools();
    if (!testDatabase.StartsWith(testPrefix) || !Guid.TryParseExact(testDatabase[testPrefix.Length..], "N", out _))
        throw new InvalidOperationException("Unexpected test database name; refusing cleanup.");
    await using var drop = new NpgsqlCommand($"DROP DATABASE {testDatabase} WITH (FORCE)", admin);
    await drop.ExecuteNonQueryAsync();
}
