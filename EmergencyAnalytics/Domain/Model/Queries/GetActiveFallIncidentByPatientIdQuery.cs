namespace foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Queries;

public record GetActiveFallIncidentByPatientIdQuery(long ActorUserId, long PatientId);
