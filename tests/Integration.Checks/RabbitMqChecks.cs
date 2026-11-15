extern alias Emergency;
extern alias Notifications;
extern alias Devices;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using E = Emergency::foll_emergency_analytics.Shared.Infrastructure.Messaging;
using N = Notifications::foll_notifications.Shared.Infrastructure.Messaging;
using D = Devices::foll_device_managment.Shared.Infrastructure.Messaging;
using NC = Notifications::foll_notifications.NotificationCommunication.Application.Contracts;
using NI = Notifications::foll_notifications.NotificationCommunication.Infrastructure.Messaging;
using EP = Emergency::foll_emergency_analytics.EmergencyAnalytics.Infrastructure.Messaging.RabbitMqEmergencyEventPublisher;
using DE = Devices::foll_device_managment.DeviceManagment.Application.Ports.DeviceEventEnvelope;
using DP = Devices::foll_device_managment.DeviceManagment.Infrastructure.Messaging.RabbitMqDeviceNotificationPublisher;
using EmergencyDb = Emergency::foll_emergency_analytics.Shared.Infrastructure.Persistence.EFC.Configuration.AppDbContext;
using NotificationDb = Notifications::foll_notifications.Shared.Infrastructure.Persistence.EFC.Configuration.AppDbContext;

static class RabbitMqChecks
{
    public static async Task RunAsync(IConfiguration source, DbContextOptions<EmergencyDb> emergencyOptions,
        DbContextOptions<NotificationDb> notificationOptions, Action<bool, string> check)
    {
        var settings = source.GetSection("RabbitMq").Get<E.RabbitMqOptions>()!;
        var suffix = Guid.NewGuid().ToString("N");
        var exchange = "foll.checks." + suffix;
        var notificationQueue = exchange + ".notifications";
        var cleanupQueue = exchange + ".emergency";
        var factory = new ConnectionFactory { HostName = settings.HostName, Port = settings.Port, UserName = settings.UserName,
            Password = settings.Password, VirtualHost = settings.VirtualHost };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true));
        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, true, false);
        await channel.QueueDeclareAsync(notificationQueue, true, false, false);
        await channel.QueueDeclareAsync(cleanupQueue, true, false, false);
        var keys = N.NotificationEventHandler.EmergencyTypes.Concat(N.NotificationEventHandler.InvitationTypes).Concat(N.NotificationEventHandler.DeviceTypes).ToArray();
        foreach (var key in keys) await channel.QueueBindAsync(notificationQueue, exchange, key);
        await channel.QueueBindAsync(cleanupQueue, exchange, E.AccountDeletionCompleted.EventType);
        try
        {
            var values = source.AsEnumerable().ToDictionary(x => x.Key, x => x.Value);
            values["RabbitMq:ExchangeName"] = exchange;
            values["IntegrationConsumers:Enabled"] = "true";
            var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            var services = new ServiceCollection().AddLogging();
            services.AddSingleton<IConfiguration>(config);
            services.AddScoped(_ => new NotificationDb(notificationOptions));
            services.AddScoped<Notifications::foll_notifications.NotificationCommunication.Application.OutboundServices.INotificationInbox, NI.NotificationInbox>();
            services.AddSingleton<NI.TelemetryBuffer>();
            N.IntegrationConsumerRegistration.AddIntegrationConsumer<N.NotificationEventHandler>(services, config, notificationQueue, keys);
            using var notifications = services.BuildServiceProvider();
            var receiver = notifications.GetRequiredService<IHostedService>();
            await receiver.StartAsync(default);

            var cleanupServices = new ServiceCollection().AddLogging();
            cleanupServices.AddScoped(_ => new EmergencyDb(emergencyOptions));
            E.IntegrationConsumerRegistration.AddIntegrationConsumer<E.PatientsDeletedHandler>(cleanupServices, config, cleanupQueue, E.AccountDeletionCompleted.EventType);
            using var emergency = cleanupServices.BuildServiceProvider();
            var cleanupReceiver = emergency.GetRequiredService<IHostedService>();
            await cleanupReceiver.StartAsync(default);
            try
            {
                var publisherOptions = new E.RabbitMqOptions { HostName = settings.HostName, Port = settings.Port, UserName = settings.UserName,
                    Password = settings.Password, VirtualHost = settings.VirtualHost, ExchangeName = exchange, QueueName = notificationQueue };
                await using var transport = new E.RabbitMqConnection(Options.Create(publisherOptions), N.NotificationEventHandler.EmergencyTypes,
                    NullLogger<E.RabbitMqConnection>.Instance);
                var publisher = new EP(transport);
                var incidentKey = Guid.NewGuid();
                var closed = new NC.EmergencyIncidentClosedIntegrationEvent(incidentKey, 1001, 25, 1, "FRONTAL", "Caida", 1,
                    NC.EmergencyIncidentStatus.Resolved, DateTime.UtcNow, null, 7, "Atendido", "Ana Perez");
                var closedEnvelope = new Emergency::foll_emergency_analytics.EmergencyAnalytics.Application.Ports.EmergencyEventEnvelope(
                    $"{NC.EmergencyAnalyticsEventTypes.IncidentClosedV1}:{incidentKey:D}", NC.EmergencyAnalyticsEventTypes.IncidentClosedV1,
                    JsonSerializer.SerializeToElement(closed), closed.ClosedAtUtc);
                await publisher.DeliverAsync(closedEnvelope, default);
                await publisher.DeliverAsync(closedEnvelope, default);
                foreach (var (kind, status) in new[] { ("created", "Pending"), ("accepted", "Accepted"), ("rejected", "Rejected") })
                {
                    var invitation = new N.CareInvitationChanged(Guid.NewGuid(), Guid.NewGuid(), 41, 25, "Paciente", 7, 8, 1,
                        "Familiar", status, DateTime.UtcNow.AddDays(1), kind, DateTime.UtcNow, 1);
                    await Publish($"care.invitation-{kind}.v1", invitation.EventId, invitation.OperationId, invitation);
                }
                var deviceOptions = new D.RabbitMqOptions { HostName = settings.HostName, Port = settings.Port, UserName = settings.UserName,
                    Password = settings.Password, VirtualHost = settings.VirtualHost, ExchangeName = exchange, QueueName = notificationQueue };
                await using var deviceTransport = new D.RabbitMqConnection(Options.Create(deviceOptions), N.NotificationEventHandler.DeviceTypes,
                    NullLogger<D.RabbitMqConnection>.Instance);
                var devices = new DP(deviceTransport);
                long outboxId = 100;
                foreach (var type in N.NotificationEventHandler.DeviceTypes.Where(x => x != DP.TelemetryType))
                {
                    outboxId++;
                    await devices.DeliverEventAsync(new DE($"device-managment:{outboxId}", outboxId, type,
                        JsonSerializer.SerializeToElement(new { DeviceId = 1001, PatientId = 25, BatteryLevel = 10, Cause = 1,
                            ReportedAtUtc = DateTime.UtcNow, DetectedAtUtc = DateTime.UtcNow, LastSeenAtUtc = DateTime.UtcNow })), default);
                }
                await devices.DeliverTelemetryAsync(new(1001, 25, 50, false, true, DateTime.UtcNow), default);
                await WaitUntil(async () => { await using var db = new NotificationDb(notificationOptions); return await db.InboxMessages.CountAsync() == 9; });
                check(true, "real RabbitMQ delivers Emergency, Care invitations and all Devices alert contracts into the durable inbox");
                await using (var db = new NotificationDb(notificationOptions))
                {
                    var result = await db.InboxMessages.SingleAsync(x => x.Type == NC.EmergencyAnalyticsEventTypes.IncidentClosedV1);
                    check(result.Payload.Contains("Ana Perez") && await db.InboxMessages.CountAsync(x => x.EventId == closedEnvelope.EventId) == 1,
                        "real Emergency publisher preserves actor name and duplicate delivery does not create a second inbox message");
                }
                await WaitUntil(() => Task.FromResult(notifications.GetRequiredService<NI.TelemetryBuffer>().Drain(DateTime.UtcNow).Count == 1));
                check(true, "real Devices publisher delivers telemetry to the ephemeral buffer");

                await using (var db = new EmergencyDb(emergencyOptions))
                {
                    db.EmergencyIncidents.Add(Emergency::foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Entities.EmergencyIncident.CreateFallDetected(
                        2002, 26, 1, DateTime.UtcNow, null, null, null, null, "{}"));
                    await db.SaveChangesAsync();
                }
                var completion = new E.AccountDeletionCompleted(Guid.NewGuid(), Guid.NewGuid(), 7, [26], DateTime.UtcNow);
                await Publish(E.AccountDeletionCompleted.EventType, completion.EventId, completion.OperationId, completion);
                await WaitUntil(async () => { await using var db = new EmergencyDb(emergencyOptions); return await db.Set<E.IntegrationInboxMessage>().CountAsync() == 2; });
                await using (var db = new EmergencyDb(emergencyOptions))
                    check(!await db.EmergencyIncidents.AnyAsync(), "real Emergency consumer commits patient cleanup before broker ACK");
            }
            finally { await receiver.StopAsync(default); await cleanupReceiver.StopAsync(default); }
        }
        finally
        {
            foreach (var queue in new[] { notificationQueue, cleanupQueue, notificationQueue + ".dead", cleanupQueue + ".dead" })
                await channel.QueueDeleteAsync(queue, false, false);
            await channel.ExchangeDeleteAsync(exchange);
        }

        async Task Publish<T>(string type, Guid id, Guid operation, T body) => await channel.BasicPublishAsync(exchange, type, true,
            new BasicProperties { Persistent = true, ContentType = "application/json", Type = type, MessageId = id.ToString(), CorrelationId = operation.ToString() },
            JsonSerializer.SerializeToUtf8Bytes(body));
    }

    private static async Task WaitUntil(Func<Task<bool>> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline) { if (await ready()) return; await Task.Delay(100); }
        throw new TimeoutException("RabbitMQ integration check did not complete.");
    }
}
