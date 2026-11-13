namespace foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Commands;

public record MarkFallIncidentFalsePositiveCommand(
    long IncidentId,
    long ActorUserId,
    string? Observation,
    DateTime MarkedAtUtc);
