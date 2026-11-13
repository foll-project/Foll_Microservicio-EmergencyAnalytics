using MediatR;
using foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Entities;

namespace foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Queries;

public record GetPatientFallsByMonthQuery(long ActorUserId, long PatientId, int Month, int Year) : IRequest<IEnumerable<EmergencyIncident>>;
