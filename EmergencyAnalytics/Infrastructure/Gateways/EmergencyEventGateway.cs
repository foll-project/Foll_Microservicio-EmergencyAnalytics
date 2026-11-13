using foll_emergency_analytics.EmergencyAnalytics.Application.Ports;

namespace foll_emergency_analytics.EmergencyAnalytics.Infrastructure.Gateways;

public class EmergencyEventGateway(HttpClient client) : IEmergencyEventGateway
{
    public async Task DeliverAsync(EmergencyEventEnvelope message, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync("internal/notifications/emergency-events", message, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
