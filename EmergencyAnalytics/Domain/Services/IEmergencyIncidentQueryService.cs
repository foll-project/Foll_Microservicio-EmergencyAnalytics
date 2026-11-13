using foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Entities;
using foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Queries;

namespace foll_emergency_analytics.EmergencyAnalytics.Domain.Services;

public interface IEmergencyIncidentQueryService
{
    Task<EmergencyIncident?> Handle(GetActiveFallIncidentByPatientIdQuery query);
    Task<EmergencyIncident?> Handle(GetEmergencyIncidentByIdQuery query);
    Task<IReadOnlyCollection<EmergencyIncident>> Handle(ListFallIncidentHistoryByPatientIdQuery query);
}
