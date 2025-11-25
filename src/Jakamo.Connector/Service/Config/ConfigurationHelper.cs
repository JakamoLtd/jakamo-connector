using Jakamo.Api.Client;

namespace Jakamo.Api.Connector.Service.Config;

public static class ConfigurationHelper
{
    /// <summary>
    /// Load configuration from multiple sources with priority order
    /// </summary>
    public static IConfiguration LoadConfiguration(string[] args)
    {
        var configBuilder = new ConfigurationBuilder();

        Console.WriteLine("=== Jakamo Connector Configuration Loading ===");
        Console.WriteLine();

        // 1. Start with appsettings.json (for defaults)
        var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(appSettingsPath))
        {
            Console.WriteLine($"[OK] Loading defaults from: {appSettingsPath}");
            configBuilder.AddJsonFile(appSettingsPath, optional: true, reloadOnChange: false);
        }
        else
        {
            Console.WriteLine($"[INFO] No appsettings.json found at: {appSettingsPath}");
        }

        // 2. Load customer config file (INI format) - search in multiple locations
        var configPaths = new[]
        {
            "/etc/jakamo-connector/jakamo-connector.conf",                      // System-wide config
            Path.Combine(AppContext.BaseDirectory, "jakamo-connector.conf"),    // Local config
            "jakamo-connector.conf"                                             // Current directory
        };

        Console.WriteLine();
        Console.WriteLine("Searching for configuration file:");
        bool configFound = false;
        
        foreach (var configPath in configPaths)
        {
            Console.WriteLine($"  Checking: {configPath}");
            if (File.Exists(configPath))
            {
                Console.WriteLine($"  [OK] Found configuration file: {configPath}");
                configBuilder.AddIniFile(configPath, optional: false, reloadOnChange: true);
                configFound = true;
                break;
            }
        }

        if (!configFound)
        {
            Console.WriteLine();
            Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                    CONFIGURATION ERROR                        ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine("ERROR: Configuration file 'jakamo-connector.conf' not found!");
            Console.WriteLine();
            Console.WriteLine("Searched in the following locations:");
            foreach (var path in configPaths)
            {
                Console.WriteLine($"  ✗ {path}");
            }
            Console.WriteLine();
            Console.WriteLine("Please ensure the configuration file exists in one of these locations.");
            Console.WriteLine("If installed via install.sh, the config should be at:");
            Console.WriteLine("  /etc/jakamo-connector/jakamo-connector.conf");
            Console.WriteLine();
            throw new FileNotFoundException(
                "Configuration file 'jakamo-connector.conf' not found. " +
                "Please check the installation or create the configuration file.");
        }

        // 3. Environment variables (override config file)
        Console.WriteLine();
        Console.WriteLine("[INFO] Loading environment variables with prefix: JAKAMO_");
        configBuilder.AddEnvironmentVariables(prefix: "JAKAMO_");

        // 4. Command line arguments (highest priority)
        if (args?.Length > 0)
        {
            Console.WriteLine($"[INFO] Loading command line arguments ({args.Length} arguments)");
            configBuilder.AddCommandLine(args);
        }

        Console.WriteLine();
        Console.WriteLine("=== Configuration Loading Complete ===");
        Console.WriteLine();

        return configBuilder.Build();
    }

    /// <summary>
    /// Map configuration to strongly-typed ConnectorConfig object
    /// </summary>
    public static ConnectorConfig GetConnectorConfig(IConfiguration configuration)
    {
        Console.WriteLine("=== Building Configuration Object ===");
        Console.WriteLine();

        try
        {
            var config = new ConnectorConfig
            {
                BaseUrl = configuration["Api:BaseUrl"] 
                          ?? throw new InvalidOperationException("Api:BaseUrl not configured"),
                Oauth2Credentials = new Oauth2Credentials()
                {
                    TenantId = configuration["Api:TenantId"] 
                               ?? throw new InvalidOperationException("Api:TenantId not configured"),
                
                    ClientId = configuration["Api:ClientId"] 
                               ?? throw new InvalidOperationException("Api:ClientId not configured"),
                
                    ClientSecret = configuration["Api:ClientSecret"] 
                                   ?? throw new InvalidOperationException("Api:ClientSecret not configured"),

                    ApiScope = configuration["Api:ApiScope"] 
                               ?? throw new InvalidOperationException("Api:ApiScope not configured"),
                },
                
                Folders = new FolderConfig
                {
                    InboundOrders = configuration["Folders:InboundOrders"] ?? "/var/lib/jakamo/inbound",
                    ProcessedOrders = configuration["Folders:ProcessedOrders"] ?? "/var/lib/jakamo/processed",
                    FailedOrders = configuration["Folders:FailedOrders"] ?? "/var/lib/jakamo/failed",
                    OrderResponses = configuration["Folders:OrderResponses"] ?? "/var/lib/jakamo/responses"
                },

                Polling = new PollingConfig
                {
                    InboundCheckIntervalSeconds = configuration.GetValue<int>("Polling:InboundCheckInterval", 30),
                    ResponseCheckIntervalSeconds = configuration.GetValue<int>("Polling:ResponseCheckInterval", 60),
                    MaxRetryAttempts = configuration.GetValue<int>("Polling:MaxRetryAttempts", 3)
                },

                Logging = new LoggingConfig
                {
                    EnableFileLogging = configuration.GetValue<bool>("Logging:EnableFileLogging", true),
                    LogFilePath = configuration["Logging:LogFile"] ?? "/var/log/jakamo/connector.log",
                    LogLevel = configuration["Logging:LogLevel"] ?? "Information"
                }
            };

            // Log configuration values (without secrets)
            Console.WriteLine("Configuration values:");
            Console.WriteLine($"  BaseUrl: {config.BaseUrl}");
            Console.WriteLine($"  TenantId: {config.Oauth2Credentials.TenantId}");
            Console.WriteLine($"  ClientId: {config.Oauth2Credentials.ClientId}");
            Console.WriteLine($"  ClientSecret: {MaskSecret(config.Oauth2Credentials.ClientSecret)}");
            Console.WriteLine($"  ApiScope: {config.Oauth2Credentials.ApiScope}");
            Console.WriteLine();
            Console.WriteLine("Folder paths:");
            Console.WriteLine($"  InboundOrders: {config.Folders.InboundOrders}");
            Console.WriteLine($"  ProcessedOrders: {config.Folders.ProcessedOrders}");
            Console.WriteLine($"  FailedOrders: {config.Folders.FailedOrders}");
            Console.WriteLine($"  OrderResponses: {config.Folders.OrderResponses}");
            Console.WriteLine();
            Console.WriteLine("Polling settings:");
            Console.WriteLine($"  InboundCheckInterval: {config.Polling.InboundCheckIntervalSeconds}s");
            Console.WriteLine($"  ResponseCheckInterval: {config.Polling.ResponseCheckIntervalSeconds}s");
            Console.WriteLine($"  MaxRetryAttempts: {config.Polling.MaxRetryAttempts}");
            Console.WriteLine();
            Console.WriteLine("Logging settings:");
            Console.WriteLine($"  EnableFileLogging: {config.Logging.EnableFileLogging}");
            Console.WriteLine($"  LogFilePath: {config.Logging.LogFilePath}");
            Console.WriteLine($"  LogLevel: {config.Logging.LogLevel}");
            Console.WriteLine();

            return config;
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine();
            Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║              CONFIGURATION MAPPING ERROR                      ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine($"ERROR: {ex.Message}");
            Console.WriteLine();
            Console.WriteLine("Please check your jakamo-connector.conf file and ensure all required");
            Console.WriteLine("settings are present and correctly formatted.");
            Console.WriteLine();
            throw;
        }
    }

    /// <summary>
    /// Validate configuration values
    /// </summary>
    public static void ValidateConfiguration(ConnectorConfig config)
    {
        Console.WriteLine("=== Validating Configuration ===");
        Console.WriteLine();
        
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.BaseUrl))
            errors.Add("Api:BaseUrl is required");

        if (string.IsNullOrWhiteSpace(config.Oauth2Credentials.TenantId))
            errors.Add("Api:TenantId is required");

        if (string.IsNullOrWhiteSpace(config.Oauth2Credentials.ClientId))
            errors.Add("Api:ClientId is required");

        if (string.IsNullOrWhiteSpace(config.Oauth2Credentials.ClientSecret))
            errors.Add("Api:ClientSecret is required");

        if (string.IsNullOrWhiteSpace(config.Oauth2Credentials.ApiScope))
            errors.Add("Api:ApiScope is required");

        if (config.Oauth2Credentials.TenantId == "YOUR_TENANT_ID_HERE")
            errors.Add("Api:TenantId must be changed from default value");

        if (config.Oauth2Credentials.ClientId == "YOUR_CLIENT_ID_HERE")
            errors.Add("Api:ClientId must be changed from default value");

        if (config.Oauth2Credentials.ClientSecret == "YOUR_CLIENT_SECRET_HERE")
            errors.Add("Api:ClientSecret must be changed from default value");

        if (config.Polling.InboundCheckIntervalSeconds < 5)
            errors.Add("Polling:InboundCheckInterval must be at least 5 seconds");

        if (config.Polling.ResponseCheckIntervalSeconds < 5)
            errors.Add("Polling:ResponseCheckInterval must be at least 5 seconds");

        if (errors.Any())
        {
            Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║               CONFIGURATION VALIDATION FAILED                 ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine("Configuration errors found:");
            foreach (var error in errors)
            {
                Console.WriteLine($"  ✗ {error}");
            }
            Console.WriteLine();
            Console.WriteLine("Please fix these issues in your configuration file:");
            Console.WriteLine("  /etc/jakamo-connector/jakamo-connector.conf");
            Console.WriteLine();
            Console.WriteLine("Then restart the service:");
            Console.WriteLine("  sudo systemctl restart jakamo-connector");
            Console.WriteLine();
            throw new InvalidOperationException(
                $"Configuration validation failed with {errors.Count} error(s). " +
                "Please check your jakamo-connector.conf file.");
        }

        Console.WriteLine("[OK] Configuration validation passed");
        Console.WriteLine();
    }

    private static string MaskSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            return "[empty]";
        
        if (secret.Length <= 8)
            return "****";
        
        return $"{secret.Substring(0, 4)}...{secret.Substring(secret.Length - 4)}";
    }
}