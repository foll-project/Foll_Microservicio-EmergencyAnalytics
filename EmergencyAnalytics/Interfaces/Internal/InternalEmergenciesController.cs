using foll_emergency_analytics.EmergencyAnalytics.Domain.Repositories;
using foll_emergency_analytics.Shared.Infrastructure.Persistence.EFC.Configuration;
using foll_emergency_analytics.Shared.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace foll_emergency_analytics.EmergencyAnalytics.Interfaces.Internal;

[ApiController, InternalApiKey]
[Route("internal/emergency")]
public class InternalEmergenciesController(AppDbContext db, IEmergencyIncidentRepository incidents) : ControllerBase
{
    [HttpGet("incidents/by-key/{incidentKey:guid}")]
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

    public sealed record PatientsDeletedRequest(IReadOnlyCollection<long> PatientIds);

    [HttpPost("patients-deleted")]
    public async Task<IActionResult> PatientsDeleted(PatientsDeletedRequest request, CancellationToken ct)
    {
        if (request.PatientIds is null || request.PatientIds.Count == 0 || request.PatientIds.Count > 1000 || request.PatientIds.Any(id => id <= 0))
            return BadRequest(new { message = "Se requieren entre 1 y 1000 identificadores positivos." });
        var ids = request.PatientIds.Distinct().ToArray();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Both processed and pending events contain patient data; avoid delivering stale alerts after deletion.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM emergency.outbox_messages WHERE (payload->>'PatientId')::bigint = ANY ({ids})", ct);
        await db.EmergencyIncidents.Where(i => ids.Contains(i.PatientId)).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        return NoContent(); // Repeating the deletion is safe.
    }
}
