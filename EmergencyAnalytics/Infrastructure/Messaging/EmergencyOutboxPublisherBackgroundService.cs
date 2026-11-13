using System.Text.Json;
using System.Text.Json.Serialization;
using foll_emergency_analytics.EmergencyAnalytics.Application.Internal.Notifications;
using foll_emergency_analytics.EmergencyAnalytics.Domain.Repositories;
using foll_emergency_analytics.Shared.Domain.Repositories;
using foll_emergency_analytics.Shared.Infrastructure.Configuration;
using MediatR;
using foll_emergency_analytics.EmergencyAnalytics.Application.Ports;
using Microsoft.Extensions.Options;

namespace foll_emergency_analytics.EmergencyAnalytics.Infrastructure.Messaging;

public class EmergencyOutboxPublisherBackgroundService : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<EmergencyOutboxPublisherBackgroundService> _logger;
    private readonly OutboxOptions _outboxOptions;

    public EmergencyOutboxPublisherBackgroundService(
        IServiceScopeFactory serviceScopeFactory,
        IOptions<OutboxOptions> outboxOptions,
        ILogger<EmergencyOutboxPublisherBackgroundService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _outboxOptions = outboxOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var outboxRepository = scope.ServiceProvider.GetRequiredService<IEmergencyOutboxMessageRepository>();
                var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
                var gateway = scope.ServiceProvider.GetRequiredService<IEmergencyEventGateway>();

                var pendingMessages = await outboxRepository.ListPendingAsync(_outboxOptions.BatchSize);
                foreach (var message in pendingMessages)
                {
                    try
                    {
                        var notification = DeserializeNotification(message.Type, message.Payload);
                        var incidentKey = notification switch
                        {
                            EmergencyIncidentOpenedIntegrationEvent opened => opened.IncidentKey,
                            EmergencyIncidentClosedIntegrationEvent closed => closed.IncidentKey,
                            _ => throw new InvalidOperationException("Evento no soportado.")
                        };
                        using var payload = JsonDocument.Parse(message.Payload);
                        await gateway.DeliverAsync(new EmergencyEventEnvelope(
                            $"{message.Type}:{incidentKey:D}", message.Type, payload.RootElement.Clone()), stoppingToken);
                        await publisher.Publish(notification, stoppingToken);
                        message.MarkProcessed(DateTime.UtcNow);
                    }
                    catch (Exception ex)
                    {
                        message.MarkFailed(ex.Message);
                        _logger.LogError(ex, "Error publicando el mensaje outbox emergency {OutboxMessageId}.", message.EmergencyOutboxMessageId);
                    }
                }

                if (pendingMessages.Count > 0)
                    await unitOfWork.CompleteAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error procesando la outbox de EmergencyAnalytics.");
            }

            await Task.Delay(TimeSpan.FromSeconds(_outboxOptions.PollIntervalSeconds), stoppingToken);
        }
    }

    private static INotification DeserializeNotification(string typeName, string payload)
    {
        return typeName switch
        {
            EmergencyAnalyticsEventTypes.IncidentOpenedV1 =>
                JsonSerializer.Deserialize<EmergencyIncidentOpenedIntegrationEvent>(payload, SerializerOptions)
                ?? throw new InvalidOperationException($"No se pudo deserializar el payload para '{typeName}'."),
            EmergencyAnalyticsEventTypes.IncidentClosedV1 =>
                JsonSerializer.Deserialize<EmergencyIncidentClosedIntegrationEvent>(payload, SerializerOptions)
                ?? throw new InvalidOperationException($"No se pudo deserializar el payload para '{typeName}'."),
            _ => throw new InvalidOperationException($"Tipo de evento emergency no soportado: '{typeName}'.")
        };
    }
}
