using Jakamo.Api.Connector.Service.Config;
using Microsoft.Extensions.Configuration;

namespace Jakamo.Connector.Tests.Service.Config;

public class ConfigurationHelperTests
{
    // -------------------------------------------------------------------------
    // ValidateConfiguration
    // -------------------------------------------------------------------------

    private static ConnectorConfig ValidConfig() => new()
    {
        BaseUrl = "https://demo.thejakamo.com",
        Oauth2Credentials = new Jakamo.Api.Client.Oauth2Credentials
        {
            ClientId = "real-client-id",
            ClientSecret = "real-client-secret",
            TenantId = "real-tenant-id",
            ApiScope = "https://demo.jakamoapp.com/.default"
        },
        Folders = new FolderConfig
        {
            InboundOrders = "/var/lib/jakamo/inbound",
            ProcessedOrders = "/var/lib/jakamo/processed",
            FailedOrders = "/var/lib/jakamo/failed",
            OrderResponses = "/var/lib/jakamo/responses"
        },
        Polling = new PollingConfig
        {
            InboundCheckIntervalSeconds = 30,
            ResponseCheckIntervalSeconds = 60,
            MaxRetryAttempts = 3
        },
        Responses = new ResponseConfig { DiscardStatusMessages = false },
        Logging = new LoggingConfig
        {
            EnableFileLogging = true,
            LogFilePath = "/var/log/jakamo/connector.log",
            LogLevel = "Information"
        }
    };

    [Fact]
    public void ValidateConfiguration_ValidConfig_DoesNotThrow()
    {
        var ex = Record.Exception(() => ConfigurationHelper.ValidateConfiguration(ValidConfig()));
        Assert.Null(ex);
    }

    [Fact]
    public void ValidateConfiguration_EmptyBaseUrl_Throws()
    {
        var config = new ConnectorConfig
        {
            BaseUrl = "",
            Oauth2Credentials = ValidConfig().Oauth2Credentials,
            Folders = ValidConfig().Folders,
            Polling = ValidConfig().Polling,
            Responses = ValidConfig().Responses,
            Logging = ValidConfig().Logging
        };
        Assert.Throws<InvalidOperationException>(() => ConfigurationHelper.ValidateConfiguration(config));
    }

    [Fact]
    public void ValidateConfiguration_PlaceholderClientId_Throws()
    {
        var config = ValidConfig();
        config.Oauth2Credentials.ClientId = "YOUR_CLIENT_ID_HERE";
        Assert.Throws<InvalidOperationException>(() => ConfigurationHelper.ValidateConfiguration(config));
    }

    [Fact]
    public void ValidateConfiguration_PlaceholderClientSecret_Throws()
    {
        var config = ValidConfig();
        config.Oauth2Credentials.ClientSecret = "YOUR_CLIENT_SECRET_HERE";
        Assert.Throws<InvalidOperationException>(() => ConfigurationHelper.ValidateConfiguration(config));
    }

    [Fact]
    public void ValidateConfiguration_PlaceholderTenantId_Throws()
    {
        var config = ValidConfig();
        config.Oauth2Credentials.TenantId = "YOUR_TENANT_ID_HERE";
        Assert.Throws<InvalidOperationException>(() => ConfigurationHelper.ValidateConfiguration(config));
    }

    [Fact]
    public void ValidateConfiguration_InboundIntervalTooShort_Throws()
    {
        var valid = ValidConfig();
        var config = new ConnectorConfig
        {
            BaseUrl = valid.BaseUrl,
            Oauth2Credentials = valid.Oauth2Credentials,
            Folders = valid.Folders,
            Polling = new PollingConfig { InboundCheckIntervalSeconds = 4, ResponseCheckIntervalSeconds = 60, MaxRetryAttempts = 3 },
            Responses = valid.Responses,
            Logging = valid.Logging
        };
        Assert.Throws<InvalidOperationException>(() => ConfigurationHelper.ValidateConfiguration(config));
    }

    [Fact]
    public void ValidateConfiguration_ResponseIntervalTooShort_Throws()
    {
        var valid = ValidConfig();
        var config = new ConnectorConfig
        {
            BaseUrl = valid.BaseUrl,
            Oauth2Credentials = valid.Oauth2Credentials,
            Folders = valid.Folders,
            Polling = new PollingConfig { InboundCheckIntervalSeconds = 30, ResponseCheckIntervalSeconds = 4, MaxRetryAttempts = 3 },
            Responses = valid.Responses,
            Logging = valid.Logging
        };
        Assert.Throws<InvalidOperationException>(() => ConfigurationHelper.ValidateConfiguration(config));
    }

    // -------------------------------------------------------------------------
    // GetConnectorConfig
    // -------------------------------------------------------------------------

    private static IConfiguration BuildInMemoryConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> FullConfigValues() => new()
    {
        ["Api:BaseUrl"] = "https://www.thejakamo.com",
        ["Api:TenantId"] = "tenant-abc",
        ["Api:ClientId"] = "client-abc",
        ["Api:ClientSecret"] = "secret-abc",
        ["Api:ApiScope"] = "https://api.jakamoapp.com/.default",
        ["Folders:InboundOrders"] = "/data/inbound",
        ["Folders:ProcessedOrders"] = "/data/processed",
        ["Folders:FailedOrders"] = "/data/failed",
        ["Folders:OrderResponses"] = "/data/responses",
        ["Polling:InboundCheckInterval"] = "15",
        ["Polling:ResponseCheckInterval"] = "45",
        ["Polling:MaxRetryAttempts"] = "5",
        ["Responses:DiscardStatusMessages"] = "true",
        ["Logging:EnableFileLogging"] = "false",
        ["Logging:LogFile"] = "/var/log/test.log",
        ["Logging:LogLevel"] = "Debug"
    };

    [Fact]
    public void GetConnectorConfig_MapsApiSection()
    {
        var config = ConfigurationHelper.GetConnectorConfig(BuildInMemoryConfig(FullConfigValues()));

        Assert.Equal("https://www.thejakamo.com", config.BaseUrl);
        Assert.Equal("tenant-abc", config.Oauth2Credentials.TenantId);
        Assert.Equal("client-abc", config.Oauth2Credentials.ClientId);
        Assert.Equal("secret-abc", config.Oauth2Credentials.ClientSecret);
        Assert.Equal("https://api.jakamoapp.com/.default", config.Oauth2Credentials.ApiScope);
    }

    [Fact]
    public void GetConnectorConfig_MapsFolderSection()
    {
        var config = ConfigurationHelper.GetConnectorConfig(BuildInMemoryConfig(FullConfigValues()));

        Assert.Equal("/data/inbound", config.Folders.InboundOrders);
        Assert.Equal("/data/processed", config.Folders.ProcessedOrders);
        Assert.Equal("/data/failed", config.Folders.FailedOrders);
        Assert.Equal("/data/responses", config.Folders.OrderResponses);
    }

    [Fact]
    public void GetConnectorConfig_MapsPollingSection()
    {
        var config = ConfigurationHelper.GetConnectorConfig(BuildInMemoryConfig(FullConfigValues()));

        Assert.Equal(15, config.Polling.InboundCheckIntervalSeconds);
        Assert.Equal(45, config.Polling.ResponseCheckIntervalSeconds);
        Assert.Equal(5, config.Polling.MaxRetryAttempts);
    }

    [Fact]
    public void GetConnectorConfig_MapsResponsesSection()
    {
        var config = ConfigurationHelper.GetConnectorConfig(BuildInMemoryConfig(FullConfigValues()));
        Assert.True(config.Responses.DiscardStatusMessages);
    }

    [Fact]
    public void GetConnectorConfig_MapsLoggingSection()
    {
        var config = ConfigurationHelper.GetConnectorConfig(BuildInMemoryConfig(FullConfigValues()));

        Assert.False(config.Logging.EnableFileLogging);
        Assert.Equal("/var/log/test.log", config.Logging.LogFilePath);
        Assert.Equal("Debug", config.Logging.LogLevel);
    }

    [Fact]
    public void GetConnectorConfig_MissingBaseUrl_Throws()
    {
        var values = FullConfigValues();
        values.Remove("Api:BaseUrl");
        Assert.Throws<InvalidOperationException>(
            () => ConfigurationHelper.GetConnectorConfig(BuildInMemoryConfig(values)));
    }

    [Fact]
    public void GetConnectorConfig_MissingClientSecret_Throws()
    {
        var values = FullConfigValues();
        values.Remove("Api:ClientSecret");
        Assert.Throws<InvalidOperationException>(
            () => ConfigurationHelper.GetConnectorConfig(BuildInMemoryConfig(values)));
    }

    [Fact]
    public void GetConnectorConfig_UsesDefaultPollingIntervals_WhenNotConfigured()
    {
        var values = FullConfigValues();
        values.Remove("Polling:InboundCheckInterval");
        values.Remove("Polling:ResponseCheckInterval");
        values.Remove("Polling:MaxRetryAttempts");

        var config = ConfigurationHelper.GetConnectorConfig(BuildInMemoryConfig(values));

        Assert.Equal(30, config.Polling.InboundCheckIntervalSeconds);
        Assert.Equal(60, config.Polling.ResponseCheckIntervalSeconds);
        Assert.Equal(3, config.Polling.MaxRetryAttempts);
    }
}
