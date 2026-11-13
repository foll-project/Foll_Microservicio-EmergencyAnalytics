namespace foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Commands;

public record UpdateEmergencyIncidentObservationCommand(
    long IncidentId,
    long ActorUserId,
    string Observation);
