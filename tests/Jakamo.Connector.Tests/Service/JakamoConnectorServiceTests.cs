using System.Net;
using Ardalis.Result;
using Jakamo.Api.Client;
using Jakamo.Api.Connector.Service;
using Jakamo.Api.Connector.Service.Config;
using Jakamo.Api.DTO;
using Jakamo.Api.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace Jakamo.Connector.Tests.Service;

public class JakamoConnectorServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _inbound;
    private readonly string _processed;
    private readonly string _failed;
    private readonly string _responses;

    public JakamoConnectorServiceTests()
    {
        _tempRoot = Directory.CreateTempSubdirectory("jakamo-tests-").FullName;
        _inbound = Path.Combine(_tempRoot, "inbound");
        _processed = Path.Combine(_tempRoot, "processed");
        _failed = Path.Combine(_tempRoot, "failed");
        _responses = Path.Combine(_tempRoot, "responses");
    }

    public void Dispose() => Directory.Delete(_tempRoot, recursive: true);

    private ConnectorConfig BuildConfig() => new()
    {
        BaseUrl = "https://demo.thejakamo.com",
        Oauth2Credentials = new Oauth2Credentials
        {
            ClientId = "test-client",
            ClientSecret = "test-secret",
            TenantId = "test-tenant",
            ApiScope = "https://demo.jakamoapp.com/.default"
        },
        Folders = new FolderConfig
        {
            InboundOrders = _inbound,
            ProcessedOrders = _processed,
            FailedOrders = _failed,
            OrderResponses = _responses
        },
        Polling = new PollingConfig
        {
            InboundCheckIntervalSeconds = 9999,
            ResponseCheckIntervalSeconds = 9999,
            MaxRetryAttempts = 3
        },
        Responses = new ResponseConfig { DiscardStatusMessages = false },
        Logging = new LoggingConfig
        {
            EnableFileLogging = false,
            LogFilePath = Path.Combine(_tempRoot, "test.log"),
            LogLevel = "Information"
        }
    };

    private JakamoConnectorService BuildService(
        Mock<IPurchaseOrderClient> mockClient,
        Mock<IHttpClientFactory>? mockHttpClientFactory = null,
        Mock<IAccessTokenProvider>? mockTokenProvider = null)
    {
        mockHttpClientFactory ??= new Mock<IHttpClientFactory>();

        var tokenProvider = mockTokenProvider ?? new Mock<IAccessTokenProvider>();
        tokenProvider.Setup(t => t.GetAccessTokenAsync()).ReturnsAsync("test-token");

        return new JakamoConnectorService(
            NullLoggerFactory.Instance,
            BuildConfig(),
            mockClient.Object,
            mockHttpClientFactory.Object,
            tokenProvider.Object);
    }

    private static (Mock<IHttpClientFactory> Factory, Mock<HttpMessageHandler> Handler) BuildHttpClientFactory(
        HttpStatusCode statusCode, string? reasonPhrase = null, string? responseBody = null)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                ReasonPhrase = reasonPhrase ?? statusCode.ToString(),
                Content = new StringContent(responseBody ?? string.Empty)
            });

        var httpClient = new HttpClient(handler.Object);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        return (factory, handler);
    }

    private Mock<IPurchaseOrderClient> DefaultMockClient()
    {
        var mock = new Mock<IPurchaseOrderClient>();
        // Responses queue is always empty unless overridden
        mock.Setup(c => c.GetOrderResponse())
            .ReturnsAsync(Result<PurchaseOrderDto>.NotFound());
        return mock;
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        Assert.True(condition(), "Expected condition was not met within timeout");
    }

    // -------------------------------------------------------------------------
    // New order
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NewOrder_ApiSucceeds_FileMovedToProcessed()
    {
        var mockClient = DefaultMockClient();
        mockClient.Setup(c => c.SendOrder(It.IsAny<Stream>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var service = BuildService(mockClient);
        await File.WriteAllTextAsync(
            Path.Combine(_inbound, "order.xml"),
            "<Order><BuyerParty>Test</BuyerParty></Order>");

        using var cts = new CancellationTokenSource();
        _ = service.StartAsync(cts.Token);

        await WaitForConditionAsync(() => !Directory.GetFiles(_inbound, "*.xml").Any());
        await cts.CancelAsync();

        Assert.Single(Directory.GetFiles(_processed));
        Assert.Empty(Directory.GetFiles(_failed));
    }

    [Fact]
    public async Task NewOrder_ApiFails_FileMovedToFailed()
    {
        var mockClient = DefaultMockClient();
        mockClient.Setup(c => c.SendOrder(It.IsAny<Stream>()))
            .ReturnsAsync(Result<bool>.Error("HTTP 422 (Unprocessable Entity): invalid xml"));

        var service = BuildService(mockClient);
        await File.WriteAllTextAsync(
            Path.Combine(_inbound, "order.xml"),
            "<Order><BuyerParty>Test</BuyerParty></Order>");

        using var cts = new CancellationTokenSource();
        _ = service.StartAsync(cts.Token);

        await WaitForConditionAsync(() => Directory.GetFiles(_failed).Any());
        await cts.CancelAsync();

        Assert.Empty(Directory.GetFiles(_processed));
        // The original file + the .error.txt file
        var failedFiles = Directory.GetFiles(_failed);
        Assert.Contains(failedFiles, f => f.EndsWith(".xml"));
        Assert.Contains(failedFiles, f => f.EndsWith(".error.txt"));
    }

    [Fact]
    public async Task NewOrder_ApiFails_ErrorFileContainsStatusCodeAndMessage()
    {
        const string apiError = "HTTP 422 (Unprocessable Entity): {\"error\":\"Schema validation failed\"}";
        var mockClient = DefaultMockClient();
        mockClient.Setup(c => c.SendOrder(It.IsAny<Stream>()))
            .ReturnsAsync(Result<bool>.Error(apiError));

        var service = BuildService(mockClient);
        await File.WriteAllTextAsync(
            Path.Combine(_inbound, "order.xml"),
            "<Order><BuyerParty>Test</BuyerParty></Order>");

        using var cts = new CancellationTokenSource();
        _ = service.StartAsync(cts.Token);

        await WaitForConditionAsync(() => Directory.GetFiles(_failed, "*.error.txt").Any());
        await cts.CancelAsync();

        var errorFile = Directory.GetFiles(_failed, "*.error.txt").Single();
        var content = await File.ReadAllTextAsync(errorFile);
        Assert.Contains("HTTP 422", content);
        Assert.Contains("Unprocessable Entity", content);
        Assert.Contains("Schema validation failed", content);
    }

    // -------------------------------------------------------------------------
    // Order update (OrderChange)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OrderChange_ApiSucceeds_CallsUpdateOrderWithCorrectId()
    {
        var mockClient = DefaultMockClient();
        mockClient.Setup(c => c.UpdateOrder("PO-001", It.IsAny<Stream>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var service = BuildService(mockClient);
        await File.WriteAllTextAsync(
            Path.Combine(_inbound, "change.xml"),
            "<OrderChange><ID>PO-001</ID></OrderChange>");

        using var cts = new CancellationTokenSource();
        _ = service.StartAsync(cts.Token);

        await WaitForConditionAsync(() => Directory.GetFiles(_processed).Any());
        await cts.CancelAsync();

        mockClient.Verify(c => c.UpdateOrder("PO-001", It.IsAny<Stream>()), Times.Once);
        Assert.Single(Directory.GetFiles(_processed));
    }

    // -------------------------------------------------------------------------
    // Status message
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StatusMessage_ApiSucceeds_CallsSendStatusMessageWithCorrectId()
    {
        var mockClient = DefaultMockClient();
        mockClient.Setup(c => c.SendStatusMessage("PO-002", It.IsAny<Stream>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var service = BuildService(mockClient);
        await File.WriteAllTextAsync(
            Path.Combine(_inbound, "status.xml"),
            "<StatusMessage><OrderID>PO-002</OrderID></StatusMessage>");

        using var cts = new CancellationTokenSource();
        _ = service.StartAsync(cts.Token);

        await WaitForConditionAsync(() => Directory.GetFiles(_processed).Any());
        await cts.CancelAsync();

        mockClient.Verify(c => c.SendStatusMessage("PO-002", It.IsAny<Stream>()), Times.Once);
        Assert.Single(Directory.GetFiles(_processed));
    }

    // -------------------------------------------------------------------------
    // Error cases
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UnknownXmlRoot_FileMovedToFailed()
    {
        var mockClient = DefaultMockClient();
        var service = BuildService(mockClient);
        await File.WriteAllTextAsync(
            Path.Combine(_inbound, "unknown.xml"),
            "<SomethingUnknown><Data>test</Data></SomethingUnknown>");

        using var cts = new CancellationTokenSource();
        _ = service.StartAsync(cts.Token);

        await WaitForConditionAsync(() => Directory.GetFiles(_failed).Any());
        await cts.CancelAsync();

        Assert.Empty(Directory.GetFiles(_processed));
        Assert.Contains(Directory.GetFiles(_failed), f => f.EndsWith(".xml"));
    }

    [Fact]
    public async Task StatusMessage_MissingOrderId_FileMovedToFailed()
    {
        var mockClient = DefaultMockClient();
        var service = BuildService(mockClient);
        // StatusMessage without an OrderID element — ExtractOrderId returns null
        await File.WriteAllTextAsync(
            Path.Combine(_inbound, "status_noid.xml"),
            "<StatusMessage><SomeOtherElement>value</SomeOtherElement></StatusMessage>");

        using var cts = new CancellationTokenSource();
        _ = service.StartAsync(cts.Token);

        await WaitForConditionAsync(() => Directory.GetFiles(_failed).Any());
        await cts.CancelAsync();

        Assert.Empty(Directory.GetFiles(_processed));
        Assert.Contains(Directory.GetFiles(_failed), f => f.EndsWith(".xml"));
    }

    [Fact]
    public async Task MultipleOrders_AllProcessed_EachMovedIndependently()
    {
        var mockClient = DefaultMockClient();
        mockClient.Setup(c => c.SendOrder(It.IsAny<Stream>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var service = BuildService(mockClient);
        await File.WriteAllTextAsync(Path.Combine(_inbound, "order1.xml"), "<Order><BuyerParty>A</BuyerParty></Order>");
        await File.WriteAllTextAsync(Path.Combine(_inbound, "order2.xml"), "<Order><BuyerParty>B</BuyerParty></Order>");
        await File.WriteAllTextAsync(Path.Combine(_inbound, "order3.xml"), "<Order><BuyerParty>C</BuyerParty></Order>");

        using var cts = new CancellationTokenSource();
        _ = service.StartAsync(cts.Token);

        await WaitForConditionAsync(() => Directory.GetFiles(_processed).Length == 3);
        await cts.CancelAsync();

        Assert.Empty(Directory.GetFiles(_inbound));
        Assert.Equal(3, Directory.GetFiles(_processed).Length);
        mockClient.Verify(c => c.SendOrder(It.IsAny<Stream>()), Times.Exactly(3));
    }

    // -------------------------------------------------------------------------
    // Attachment upload
    // -------------------------------------------------------------------------

    private string AttachmentsDir => Path.Combine(_inbound, "attachments");

    [Fact]
    public async Task Attachment_ApiSucceeds_FileMovedToProcessed()
    {
        var (factory, _) = BuildHttpClientFactory(HttpStatusCode.OK);
        var service = BuildService(DefaultMockClient(), factory);
        await File.WriteAllBytesAsync(Path.Combine(AttachmentsDir, "PO-001_invoice.pdf"), [1, 2, 3]);

        using var cts = new CancellationTokenSource();
        _ = service.StartAsync(cts.Token);

        await WaitForConditionAsync(() => Directory.GetFiles(_processed).Any());
        await cts.CancelAsync();

        Assert.Single(Directory.GetFiles(_processed));
        Assert.Empty(Directory.GetFiles(_failed));
    }

    [Fact]
    public async Task Attachment_ApiFails_FileMovedToFailed_WithErrorFile()
    {
        var (factory, _) = BuildHttpClientFactory(
            HttpStatusCode.UnprocessableEntity,
            reasonPhrase: "Unprocessable Entity",
            responseBody: "Order not found");
        var service = BuildService(DefaultMockClient(), factory);
        await File.WriteAllBytesAsync(Path.Combine(AttachmentsDir, "PO-001_invoice.pdf"), [1, 2, 3]);

        using var cts = new CancellationTokenSource();
        _ = service.StartAsync(cts.Token);

        await WaitForConditionAsync(() => Directory.GetFiles(_failed, "*.error.txt").Any());
        await cts.CancelAsync();

        var errorContent = await File.ReadAllTextAsync(Directory.GetFiles(_failed, "*.error.txt").Single());
        Assert.Contains("422", errorContent);
        Assert.Contains("Unprocessable Entity", errorContent);
        Assert.Contains("Order not found", errorContent);
    }

    [Fact]
    public async Task Attachment_PostsToCorrectOrderUrl()
    {
        var (factory, handler) = BuildHttpClientFactory(HttpStatusCode.OK);
        var service = BuildService(DefaultMockClient(), factory);
        await File.WriteAllBytesAsync(Path.Combine(AttachmentsDir, "PO-001_invoice.pdf"), [1, 2, 3]);

        using var cts = new CancellationTokenSource();
        _ = service.StartAsync(cts.Token);

        await WaitForConditionAsync(() => Directory.GetFiles(_processed).Any());
        await cts.CancelAsync();

        handler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Post &&
                r.RequestUri!.ToString().Contains("/order/PO-001/attachment")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task Attachment_FilenameWithoutUnderscore_MovesToFailed()
    {
        var (factory, _) = BuildHttpClientFactory(HttpStatusCode.OK);
        var service = BuildService(DefaultMockClient(), factory);
        await File.WriteAllBytesAsync(Path.Combine(AttachmentsDir, "invoice.pdf"), [1, 2, 3]);

        using var cts = new CancellationTokenSource();
        _ = service.StartAsync(cts.Token);

        await WaitForConditionAsync(() => Directory.GetFiles(_failed).Any());
        await cts.CancelAsync();

        Assert.Empty(Directory.GetFiles(_processed));
        Assert.Contains(Directory.GetFiles(_failed), f => f.EndsWith(".pdf"));
    }

    [Fact]
    public async Task Attachment_OrderNumberIsEverythingBeforeFirstUnderscore()
    {
        // PO_123_file.pdf → order number is "PO", not "PO_123"
        var (factory, handler) = BuildHttpClientFactory(HttpStatusCode.OK);
        var service = BuildService(DefaultMockClient(), factory);
        await File.WriteAllBytesAsync(Path.Combine(AttachmentsDir, "PO_123_file.pdf"), [1, 2, 3]);

        using var cts = new CancellationTokenSource();
        _ = service.StartAsync(cts.Token);

        await WaitForConditionAsync(() => Directory.GetFiles(_processed).Any());
        await cts.CancelAsync();

        handler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.RequestUri!.ToString().Contains("/order/PO/attachment")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task Attachment_BearerTokenIsIncludedInRequest()
    {
        var (factory, handler) = BuildHttpClientFactory(HttpStatusCode.OK);
        var service = BuildService(DefaultMockClient(), factory);
        await File.WriteAllBytesAsync(Path.Combine(AttachmentsDir, "PO-001_doc.pdf"), [1, 2, 3]);

        using var cts = new CancellationTokenSource();
        _ = service.StartAsync(cts.Token);

        await WaitForConditionAsync(() => Directory.GetFiles(_processed).Any());
        await cts.CancelAsync();

        handler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Headers.Authorization != null &&
                r.Headers.Authorization.Scheme == "Bearer" &&
                r.Headers.Authorization.Parameter == "test-token"),
            ItExpr.IsAny<CancellationToken>());
    }
}
