using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using foll_emergency_analytics.EmergencyAnalytics.Application.Ports;
using foll_emergency_analytics.Shared.Infrastructure.Messaging;

namespace foll_emergency_analytics.EmergencyAnalytics.Infrastructure.Messaging;

public record EmergencyBrokerEvent(Guid EventId, Guid OperationId, DateTime OccurredAtUtc,
    string EventType, JsonElement Payload, int Version = 1);

public sealed class RabbitMqEmergencyEventPublisher(RabbitMqConnection connection) : IEmergencyEventGateway
{
    public static EmergencyBrokerEvent CreateMessage(EmergencyEventEnvelope message)
    {
        if (message.Type is not ("emergency-analytics.incident-opened.v1" or "emergency-analytics.incident-closed.v1"))
            throw new InvalidDataException("Evento de Emergency no soportado.");
        var key = message.Payload.GetProperty("IncidentKey").GetGuid();
        if (key == Guid.Empty || message.EventId != $"{message.Type}:{key:D}" || message.OccurredAtUtc.Kind != DateTimeKind.Utc)
            throw new InvalidDataException("Identidad o fecha del evento de Emergency invalida.");
        var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(message.EventId)).AsSpan(0, 16));
        return new(id, key, message.OccurredAtUtc, message.Type, message.Payload.Clone());
    }

    public Task DeliverAsync(EmergencyEventEnvelope message, CancellationToken cancellationToken)
    {
        var value = CreateMessage(message);
        return connection.PublishAsync(value.EventType, value.EventId, value.OperationId, value.OccurredAtUtc,
            JsonSerializer.SerializeToUtf8Bytes(value), cancellationToken);
    }
}
