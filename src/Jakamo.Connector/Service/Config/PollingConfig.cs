namespace Jakamo.Api.Connector.Service.Config;

public class PollingConfig
{
    public int InboundCheckIntervalSeconds { get; set; }
    public int ResponseCheckIntervalSeconds { get; set; }
    public int MaxRetryAttempts { get; set; }
}

