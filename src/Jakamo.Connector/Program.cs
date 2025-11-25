using Jakamo.Api.Client;
using Jakamo.Api.Connector;
using Jakamo.Api.Connector.Service;
using Jakamo.Api.Connector.Service.Config;
using Jakamo.Api.Interfaces;

var builder = Host.CreateApplicationBuilder(args);

// Load and validate configuration
var customConfig = ConfigurationHelper.LoadConfiguration(args);
var connectorConfig = ConfigurationHelper.GetConnectorConfig(customConfig);
ConfigurationHelper.ValidateConfiguration(connectorConfig);
builder.Services.AddSingleton(connectorConfig);

// Setup logging
builder.ConfigureLogging(connectorConfig);

// Register the background service
builder.Services.AddHostedService<JakamoConnectorService>();

// Register a purchase order client
builder.Services.AddHttpClient<IPurchaseOrderClient, PurchaseOrderClient>((httpClient, sp) =>
{
    var config = sp.GetRequiredService<ConnectorConfig>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    
    return new PurchaseOrderClient(
        httpClient,
        new Uri(config.BaseUrl),
        config.Oauth2Credentials,
        loggerFactory);
});

// Enable systemd/Windows Service support
builder.Services.AddSystemd();
builder.Services.AddWindowsService();

// Register http client factory
builder.Services.AddHttpClient();

// Build and run the host
var host = builder.Build();
host.Run();