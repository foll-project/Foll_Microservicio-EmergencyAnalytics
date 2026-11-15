using System.Net;
using System.Net.Http.Json;
using foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Entities;
using foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Enums;
using foll_emergency_analytics.EmergencyAnalytics.Infrastructure.Gateways;
using foll_emergency_analytics.EmergencyAnalytics.Application.Ports;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Foll.EmergencyAnalytics.Tests;

public class DomainAndGatewayTests
{
    private static EmergencyIncident Incident() => EmergencyIncident.CreateFallDetected(
        1001, 25, 1, DateTime.UtcNow, .95m, -12.05m, -77.04m, "Lima", "{}");

    [Fact]
    public void ResolutionPreservesAuditAndCannotBeRepeated()
    {
        var incident = Incident();
        incident.Resolve(7, " Atendido ", DateTime.UtcNow);
        Assert.Equal(EmergencyIncidentStatus.Resolved, incident.Status);
        Assert.Equal(7, incident.ClosedByUserId);
        Assert.Equal("Atendido", incident.FinalObservation);
        Assert.Throws<InvalidOperationException>(() => incident.Resolve(7, null, DateTime.UtcNow));
    }

    [Fact]
    public void FalsePositiveAndButtonCancellationKeepDistinctReasons()
    {
        var falsePositive = Incident();
        falsePositive.MarkAsFalsePositive(7, "Sin caída", DateTime.UtcNow);
        var button = Incident();
        button.Cancel(EmergencyCancellationReason.UserButtonPressed, DateTime.UtcNow, "{}");
        Assert.Equal(EmergencyCancellationReason.FalsePositive, falsePositive.CancellationReason);
        Assert.Equal(EmergencyCancellationReason.UserButtonPressed, button.CancellationReason);
        Assert.Null(button.ClosedByUserId);
    }

    [Fact]
    public void OlderDetectionCannotOverwriteLatestLocation()
    {
        var incident = Incident();
        incident.RefreshDetection(2, incident.OpenedAt.AddMinutes(-1), .1m, 1, 2, "Otro lugar", "{}");
        Assert.Equal("Lima", incident.Address);
        Assert.Equal((short)1, incident.FallTypeId);
        Assert.Throws<InvalidOperationException>(() => incident.UpdateFinalObservation(7, "No cerrado"));
    }

    [Theory]
    [InlineData(7, 25, true)] // official guardian
    [InlineData(8, 25, true)] // current guardian
    [InlineData(9, 25, true)] // caregiver
    [InlineData(10, 25, false)]
    [InlineData(7, 26, false)] // unexpected patient DTO fails closed
    public async Task CareV1DtoIsAuthorizedLocally(long actor, long patientId, bool expected)
    {
        using var client = Client(request =>
        {
            Assert.Equal("Bearer test-token", request.Headers.Authorization?.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
            {
                patientId = 25, officialGuardianUserId = 7, currentGuardianUserId = 8, caregiverUserIds = new long[] { 9 }
            }) };
        });
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer test-token";
        var gateway = new PatientIncidentAccessGateway(client, new HttpContextAccessor { HttpContext = context });
        Assert.Equal(expected, await gateway.CanAccessIncidentAsync(actor, patientId));
    }

    [Fact]
    public async Task CareGatewayDoesNotCallWithoutUserToken()
    {
        using var client = Client(_ => throw new Exception("Must not call upstream"));
        var gateway = new PatientIncidentAccessGateway(client, new HttpContextAccessor());
        Assert.False(await gateway.CanAccessIncidentAsync(7, 25));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task MissingOrDeniedPatientFailsClosed(HttpStatusCode status)
    {
        using var client = Client(_ => new HttpResponseMessage(status));
        var context = new DefaultHttpContext(); context.Request.Headers.Authorization = "Bearer test-token";
        Assert.False(await new PatientIncidentAccessGateway(client, new HttpContextAccessor { HttpContext = context })
            .CanAccessIncidentAsync(7, 25));
    }

    [Fact]
    public async Task UpstreamFailureIsNotMistakenForPatientNotFound()
    {
        using var client = Client(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await Assert.ThrowsAsync<HttpRequestException>(() => new DeviceIncidentAssignmentGateway(client).FindAssignedPatientIdAsync(1001));
    }

    [Fact]
    public async Task AssignmentContractSupportsUnlinkedDevice()
    {
        using var client = Client(request =>
        {
            Assert.Equal("/internal/devices/1001/assignment", request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { deviceId = 1001, assignedPatientId = (long?)null }) };
        });
        Assert.Null(await new DeviceIncidentAssignmentGateway(client).FindAssignedPatientIdAsync(1001));
    }

    internal static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> action) =>
        new(new Handler(action)) { BaseAddress = new Uri("http://test/") };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(action(request));
    }
}
