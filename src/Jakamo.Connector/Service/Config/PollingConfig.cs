namespace Jakamo.Api.Connector.Service.Config;

public class PollingConfig
{
    public required int InboundCheckIntervalSeconds { get; init; }
    public required int ResponseCheckIntervalSeconds { get; init; }
    public required int MaxRetryAttempts { get; init; }
}

