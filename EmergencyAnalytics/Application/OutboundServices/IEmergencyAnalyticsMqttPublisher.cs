namespace foll_emergency_analytics.EmergencyAnalytics.Application.OutboundServices;

public interface IEmergencyAnalyticsMqttPublisher
{
    Task PublishIncidentClosedAsync(
        long deviceId,
        string status,
        string? cancellationReason,
        DateTime closedAtUtc,
        CancellationToken cancellationToken = default);
}
