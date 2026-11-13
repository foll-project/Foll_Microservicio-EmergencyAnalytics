using System.Net;
using System.Net.Http.Headers;
using foll_emergency_analytics.EmergencyAnalytics.Application.OutboundServices;

namespace foll_emergency_analytics.EmergencyAnalytics.Infrastructure.Gateways;

public class PatientIncidentAccessGateway(HttpClient client, IHttpContextAccessor accessor)
    : IPatientIncidentAccessService
{
    public async Task<bool> CanAccessIncidentAsync(long actorUserId, long patientId)
    {
        if (actorUserId <= 0 || patientId <= 0) return false;
        // Authorization is evaluated here even when Care v1 returns an unrestricted access DTO.
        var header = accessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header)) return false;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"internal/patients/{patientId}/emergency-access");
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(header);
        using var response = await client.SendAsync(request);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            return false;
        response.EnsureSuccessStatusCode();
        var access = await response.Content.ReadFromJsonAsync<PatientAccess>();
        return access is not null && access.PatientId == patientId &&
            (access.OfficialGuardianUserId == actorUserId || access.CurrentGuardianUserId == actorUserId ||
             access.CaregiverUserIds?.Contains(actorUserId) == true);
    }

    private sealed record PatientAccess(long PatientId, long OfficialGuardianUserId,
        long? CurrentGuardianUserId, IReadOnlyCollection<long>? CaregiverUserIds);
}
