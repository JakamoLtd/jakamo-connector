namespace Jakamo.Api.Connector.Service.Config;

public class LoggingConfig
{
    public required bool EnableFileLogging { get; init; }
    public required string LogFilePath { get; init; }
    public required string LogLevel { get; init; }
}