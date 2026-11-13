using System.Net;
using foll_emergency_analytics.EmergencyAnalytics.Application.OutboundServices;

namespace foll_emergency_analytics.EmergencyAnalytics.Infrastructure.Gateways;

public class DeviceIncidentAssignmentGateway(HttpClient client) : IDeviceIncidentAssignmentService
{
    public async Task<long?> FindAssignedPatientIdAsync(long deviceId)
    {
        if (deviceId <= 0) return null;
        using var response = await client.GetAsync($"internal/devices/{deviceId}/assignment");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var assignment = await response.Content.ReadFromJsonAsync<DeviceAssignment>();
        return assignment?.AssignedPatientId;
    }

    private sealed record DeviceAssignment(long DeviceId, long? AssignedPatientId);
}
