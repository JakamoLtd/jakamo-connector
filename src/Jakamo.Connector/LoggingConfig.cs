using Jakamo.Api.Connector.Service.Config;
using Serilog;

namespace Jakamo.Api.Connector;

public static class LoggingConfig
{
    public static void ConfigureLogging(this HostApplicationBuilder builder, ConnectorConfig config)
    {
        // Configure Serilog
        var loggerConfig = new LoggerConfiguration()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
        
        // Write logs to file if enabled
        if (config.Logging.EnableFileLogging && !string.IsNullOrWhiteSpace(config.Logging.LogFilePath))
        {
            var logFilePath = config.Logging.LogFilePath;
            loggerConfig.WriteTo.File(
                logFilePath,
                rollingInterval: RollingInterval.Day,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
        }
        
        // Set log level, default to information
        if (config.Logging.LogLevel.Equals("Debug", StringComparison.OrdinalIgnoreCase))
        {
            loggerConfig.MinimumLevel.Debug();
        }
        else
        {
            loggerConfig.MinimumLevel.Information();
            loggerConfig.MinimumLevel.Override("System.Net.Http", Serilog.Events.LogEventLevel.Warning);
        }

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(loggerConfig.CreateLogger());
    }
}