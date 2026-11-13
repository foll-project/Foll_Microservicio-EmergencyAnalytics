namespace foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Queries;

public record ListFallIncidentHistoryByPatientIdQuery(long ActorUserId, long PatientId);
