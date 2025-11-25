using Jakamo.Api.Client;

namespace Jakamo.Api.Connector.Service.Config;

public class ConnectorConfig
{
    public string BaseUrl { get; set; }
    public Oauth2Credentials Oauth2Credentials { get; set; }
    public FolderConfig Folders { get; set; }
    public PollingConfig Polling { get; set; }
    public LoggingConfig Logging { get; set; }
}

