using foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Commands;

namespace foll_emergency_analytics.EmergencyAnalytics.Domain.Services;

public interface IEmergencyIncidentCommandService
{
    Task Handle(RegisterFallDetectedCommand command);
    Task Handle(RegisterFallCancelledCommand command);
    Task Handle(MarkFallIncidentFalsePositiveCommand command);
    Task Handle(ResolveFallIncidentCommand command);
    Task Handle(UpdateEmergencyIncidentObservationCommand command);
    Task Handle(DeleteEmergenciesByAccountCommand command);
}
