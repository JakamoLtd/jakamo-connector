namespace Jakamo.Api.Connector.Service.Config;

public class FolderConfig
{
    public required string InboundOrders { get; init; }
    public required string ProcessedOrders { get; init; }
    public required string FailedOrders { get; init; }
    public required string OrderResponses { get; init; }
}