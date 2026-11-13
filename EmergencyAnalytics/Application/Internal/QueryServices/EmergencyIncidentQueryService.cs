using foll_emergency_analytics.EmergencyAnalytics.Application.OutboundServices;
using foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Entities;
using foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Queries;
using foll_emergency_analytics.EmergencyAnalytics.Domain.Repositories;
using foll_emergency_analytics.EmergencyAnalytics.Domain.Services;

namespace foll_emergency_analytics.EmergencyAnalytics.Application.Internal.QueryServices;

public class EmergencyIncidentQueryService : IEmergencyIncidentQueryService
{
    private readonly IEmergencyIncidentRepository _incidentRepository;
    private readonly IPatientIncidentAccessService _patientIncidentAccessService;

    public EmergencyIncidentQueryService(
        IEmergencyIncidentRepository incidentRepository,
        IPatientIncidentAccessService patientIncidentAccessService)
    {
        _incidentRepository = incidentRepository;
        _patientIncidentAccessService = patientIncidentAccessService;
    }

    public async Task<EmergencyIncident?> Handle(GetActiveFallIncidentByPatientIdQuery query)
    {
        if (!await _patientIncidentAccessService.CanAccessIncidentAsync(query.ActorUserId, query.PatientId))
            throw new InvalidOperationException("No tienes permisos para consultar incidentes de este paciente.");

        return await _incidentRepository.FindActiveByPatientIdAsync(query.PatientId);
    }

    public async Task<EmergencyIncident?> Handle(GetEmergencyIncidentByIdQuery query)
    {
        var incident = await _incidentRepository.FindByIdWithFallTypeAsync(query.IncidentId);
        if (incident is null) return null;

        if (!await _patientIncidentAccessService.CanAccessIncidentAsync(query.ActorUserId, incident.PatientId))
            throw new InvalidOperationException("No tienes permisos para consultar este incidente.");

        return incident;
    }

    public async Task<IReadOnlyCollection<EmergencyIncident>> Handle(ListFallIncidentHistoryByPatientIdQuery query)
    {
        if (!await _patientIncidentAccessService.CanAccessIncidentAsync(query.ActorUserId, query.PatientId))
            throw new InvalidOperationException("No tienes permisos para consultar incidentes de este paciente.");

        return await _incidentRepository.ListByPatientIdAsync(query.PatientId);
    }
}
