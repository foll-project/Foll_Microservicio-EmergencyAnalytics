using foll_emergency_analytics.Shared.Domain.Events;

namespace foll_emergency_analytics.EmergencyAnalytics.Domain.Events;

public record EmergencyIncidentOpenedDomainEvent(
    Guid IncidentKey,
    long DeviceId,
    long PatientId,
    short FallTypeId,
    DateTime OpenedAtUtc,
    decimal? AiConfidenceScore,
    decimal? Latitude,
    decimal? Longitude,
    string? Address) : IDomainEvent
{
    public DateTime OccurredOn => OpenedAtUtc;
}
