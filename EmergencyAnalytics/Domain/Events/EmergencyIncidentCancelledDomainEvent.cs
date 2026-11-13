using foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Enums;
using foll_emergency_analytics.Shared.Domain.Events;

namespace foll_emergency_analytics.EmergencyAnalytics.Domain.Events;

public record EmergencyIncidentCancelledDomainEvent(
    Guid IncidentKey,
    long DeviceId,
    long PatientId,
    short FallTypeId,
    DateTime CancelledAtUtc,
    EmergencyCancellationReason Reason,
    long? ActorUserId,
    string? Observation) : IDomainEvent
{
    public DateTime OccurredOn => CancelledAtUtc;
}
