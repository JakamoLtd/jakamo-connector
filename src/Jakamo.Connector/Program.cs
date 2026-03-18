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

// Register the token provider (shared between the API client and the service)
builder.Services.AddSingleton<IAccessTokenProvider>(sp =>
{
    var config = sp.GetRequiredService<ConnectorConfig>();
    var creds = config.Oauth2Credentials;
    return new Oauth2AccessTokenProvider(creds.ClientId, creds.ClientSecret, creds.TenantId, creds.ApiScope);
});

// Register a purchase order client
builder.Services.AddHttpClient<IPurchaseOrderClient, PurchaseOrderClient>((httpClient, sp) =>
{
    var config = sp.GetRequiredService<ConnectorConfig>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var tokenProvider = sp.GetRequiredService<IAccessTokenProvider>();

    return new PurchaseOrderClient(
        httpClient,
        new Uri(config.BaseUrl),
        tokenProvider,
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