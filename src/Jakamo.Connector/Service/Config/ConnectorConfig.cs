using Jakamo.Api.Client;

namespace Jakamo.Api.Connector.Service.Config;

public class ConnectorConfig
{
    public required string BaseUrl { get; init; }
    public required Oauth2Credentials Oauth2Credentials { get; init; }
    public required FolderConfig Folders { get; init; }
    public required PollingConfig Polling { get; init; }
    public required LoggingConfig Logging { get; init; }
}

