using foll_emergency_analytics.Shared.Domain.Events;

namespace foll_emergency_analytics.EmergencyAnalytics.Domain.Events;

public record EmergencyIncidentResolvedDomainEvent(
    Guid IncidentKey,
    long DeviceId,
    long PatientId,
    short FallTypeId,
    DateTime ResolvedAtUtc,
    long ActorUserId,
    string? Observation) : IDomainEvent
{
    public DateTime OccurredOn => ResolvedAtUtc;
}
