namespace foll_emergency_analytics.Shared.Infrastructure.Messaging;

public sealed class RabbitMqOptions : BrokerSettings
{
    public string QueueName { get; set; } = "notificationcommunication.emergency-events.v1";
}
