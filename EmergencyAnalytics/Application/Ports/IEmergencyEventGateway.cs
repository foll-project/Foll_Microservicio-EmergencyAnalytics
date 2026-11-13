using System.Text.Json;

namespace foll_emergency_analytics.EmergencyAnalytics.Application.Ports;

public record EmergencyEventEnvelope(string EventId, string Type, JsonElement Payload);

public interface IEmergencyEventGateway
{
    Task DeliverAsync(EmergencyEventEnvelope message, CancellationToken cancellationToken);
}
