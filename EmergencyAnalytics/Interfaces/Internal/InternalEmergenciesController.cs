using foll_emergency_analytics.EmergencyAnalytics.Domain.Repositories;
using Microsoft.AspNetCore.Mvc;
using foll_emergency_analytics.Shared.Infrastructure.Security;

namespace foll_emergency_analytics.EmergencyAnalytics.Interfaces.Internal;

[ApiController]
[Route("internal/emergency")]
public class InternalEmergenciesController(IEmergencyIncidentRepository incidents) : ControllerBase
{
    [HttpGet("incidents/by-key/{incidentKey:guid}")]
    [BackgroundQueryKey]
    public async Task<IActionResult> ByKey(Guid incidentKey)
    {
        var incident = await incidents.FindByIncidentKeyWithFallTypeAsync(incidentKey);
        if (incident is null) return NotFound();
        return Ok(new
        {
            incident.IncidentKey, incident.PatientId, incident.DeviceId,
            status = incident.Status.ToString(), incident.OpenedAt,
            fallType = incident.FallType is null ? null : new { incident.FallType.Name, incident.FallType.SeverityLevel },
            location = new { incident.Latitude, incident.Longitude }
        });
    }

}
