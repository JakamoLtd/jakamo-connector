using System.Net.Http.Headers;
using System.Xml.Linq;
using Ardalis.Result;
using Jakamo.Api.Client;
using Jakamo.Api.Connector.Service.Config;
using Jakamo.Api.Interfaces;

namespace Jakamo.Api.Connector.Service;
public class JakamoConnectorService : BackgroundService
{
    private const string AttachmentsSubfolder = "attachments";

    private readonly ILogger<JakamoConnectorService> _logger;
    private readonly IPurchaseOrderClient _client;
    private readonly ConnectorConfig _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAccessTokenProvider _tokenProvider;

    public JakamoConnectorService(
        ILoggerFactory loggerFactory,
        ConnectorConfig config,
        IPurchaseOrderClient client,
        IHttpClientFactory httpClientFactory,
        IAccessTokenProvider tokenProvider)
    {
        _logger = loggerFactory.CreateLogger<JakamoConnectorService>();

        _config = config;
        _client = client;
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;

        // Ensure directories exist
        EnsureDirectoriesExist();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Jakamo Connector Service started");
        _logger.LogInformation("Monitoring folder: {Folder}", _config.Folders.InboundOrders);
        _logger.LogInformation("Response folder: {Folder}", _config.Folders.OrderResponses);

        var attachmentDir = Path.Combine(_config.Folders.InboundOrders, AttachmentsSubfolder);
        _logger.LogInformation("Attachment folder: {Folder}", attachmentDir);

        var inboundTask = PollInboundOrders(stoppingToken);
        var responseTask = PollOrderResponses(stoppingToken);
        var attachmentTask = PollAttachments(stoppingToken);

        await Task.WhenAll(inboundTask, responseTask, attachmentTask);
    }

    private async Task PollInboundOrders(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessInboundOrders();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing inbound orders");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_config.Polling.InboundCheckIntervalSeconds),
                stoppingToken);
        }
    }

    private async Task PollOrderResponses(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOrderResponses();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing order responses");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_config.Polling.ResponseCheckIntervalSeconds),
                stoppingToken);
        }
    }

    private async Task PollAttachments(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessAttachments();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing attachments");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_config.Polling.InboundCheckIntervalSeconds),
                stoppingToken);
        }
    }

    private async Task ProcessAttachments()
    {
        var attachmentDir = Path.Combine(_config.Folders.InboundOrders, AttachmentsSubfolder);
        var files = Directory.GetFiles(attachmentDir);

        if (files.Length == 0)
            return;

        _logger.LogInformation("Found {Count} attachment file(s) to process", files.Length);

        foreach (var filePath in files)
            await ProcessSingleAttachment(filePath);
    }

    private async Task ProcessSingleAttachment(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        _logger.LogInformation("Processing attachment: {FileName}", fileName);

        try
        {
            var orderNumber = ExtractOrderNumberFromFilename(fileName);
            var (success, error) = await UploadAttachment(filePath, fileName, orderNumber);

            if (success)
            {
                MoveToProcessed(filePath);
                _logger.LogInformation("✓ Successfully uploaded attachment: {FileName} to order {OrderNumber}", fileName, orderNumber);
            }
            else
            {
                MoveToFailed(filePath, error);
                _logger.LogWarning("✗ Failed to upload attachment: {FileName}", fileName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing attachment {FileName}", fileName);
            MoveToFailed(filePath, ex.ToString());
        }
    }

    private static string ExtractOrderNumberFromFilename(string fileName)
    {
        var underscoreIndex = fileName.IndexOf('_');
        if (underscoreIndex <= 0)
            throw new InvalidOperationException(
                $"Cannot determine order number from '{fileName}'. Expected format: {{OrderNumber}}_{{FileName}}");

        return fileName[..underscoreIndex];
    }

    private async Task<(bool Success, string? Error)> UploadAttachment(string filePath, string fileName, string orderNumber)
    {
        var token = await _tokenProvider.GetAccessTokenAsync();
        var httpClient = _httpClientFactory.CreateClient(string.Empty);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var fileStream = File.OpenRead(filePath);
        using var formData = new MultipartFormDataContent();
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(fileName));
        formData.Add(fileContent, "Attachment", fileName);

        var url = $"{_config.BaseUrl.TrimEnd('/')}/api/order/{orderNumber}/attachment";
        var response = await httpClient.PostAsync(url, formData);

        if (response.IsSuccessStatusCode)
            return (true, null);

        var body = await response.Content.ReadAsStringAsync();
        var error = $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {body}";
        _logger.LogError("Failed to upload attachment {FileName}: {Error}", fileName, error);
        return (false, error);
    }

    private static string GetContentType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf"  => "application/pdf",
            ".xml"  => "application/xml",
            ".json" => "application/json",
            ".txt"  => "text/plain",
            ".csv"  => "text/csv",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xls"  => "application/vnd.ms-excel",
            ".zip"  => "application/zip",
            ".png"  => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };

    private static string? SanitizeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c));
    }

    private async Task ProcessInboundOrders()
    {
        var xmlFiles = Directory.GetFiles(_config.Folders.InboundOrders, "*.xml");

        if (xmlFiles.Length == 0)
        {
            return;
        }

        _logger.LogInformation("Found {Count} XML files to process", xmlFiles.Length);

        foreach (var filePath in xmlFiles)
        {
            await ProcessSingleOrder(filePath);
        }
    }

    private async Task ProcessSingleOrder(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        _logger.LogInformation("Processing: {FileName}", fileName);

        try
        {
            // Detect message type from XML content
            var messageType = DetectMessageType(filePath);
            _logger.LogInformation("Detected message type: {Type}", messageType);

            // Extract order ID if needed
            string? orderId = null;
            if (messageType != MessageType.NewOrder)
            {
                orderId = ExtractOrderId(filePath);
                if (orderId is null)
                {
                    throw new InvalidOperationException("Could not extract order ID from update/status message");
                }
            }

            // Send to Jakamo
            var (success, apiError) = await SendMessage(filePath, messageType, orderId);

            if (success)
            {
                MoveToProcessed(filePath);
                _logger.LogInformation("✓ Successfully processed: {FileName}", fileName);
            }
            else
            {
                MoveToFailed(filePath, apiError);
                _logger.LogWarning("✗ Failed to process: {FileName}", fileName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing {FileName}", fileName);
            MoveToFailed(filePath, ex.ToString());
        }
    }

    private async Task<(bool Success, string? Error)> SendMessage(string filePath, MessageType messageType, string? orderId)
    {
        using var fileStream = File.OpenRead(filePath);

        var result = messageType switch
        {
            MessageType.NewOrder => await _client.SendOrder(fileStream),
            MessageType.OrderUpdate => await _client.UpdateOrder(orderId, fileStream),
            MessageType.StatusMessage => await _client.SendStatusMessage(orderId, fileStream),
            _ => throw new ArgumentException($"Unknown message type: {messageType}")
        };

        if (!result.IsSuccess)
        {
            var error = string.Join(", ", result.Errors);
            _logger.LogError("API Error: {Errors}", error);
            return (false, error);
        }

        return (true, null);
    }

    private async Task ProcessOrderResponses()
    {
        while (true)
        {
            var result = await _client.GetOrderResponse();

            if (result.Status == ResultStatus.NotFound)
                break;

            if (!result.IsSuccess)
            {
                _logger.LogError("Failed to fetch order response: {Errors}",
                    string.Join(", ", result.Errors));
                break;
            }

            try
            {
                var doc = XDocument.Load(result.Value.XmlStream);
                var rootName = doc.Root?.Name.LocalName;
                var ackUri = result.Value.AcknowledgementUri;

                switch (rootName?.ToLowerInvariant())
                {
                    case "orderresponse":
                        await SaveOrderResponse(doc, result.Value.OrderNumber, ackUri);
                        break;
                    case "statusmessage":
                        if (_config.Responses.DiscardStatusMessages)
                        {
                            _logger.LogInformation("Discarding status message (DiscardStatusMessages=true)");
                            await AcknowledgeResponse(ackUri, null);
                        }
                        else
                        {
                            await SaveStatusMessage(doc, ackUri);
                        }
                        break;
                    case "attachment":
                        await SaveAttachment(doc, ackUri);
                        break;
                    default:
                        _logger.LogWarning("Unknown response type with root element: {RootElement}", rootName);
                        await AcknowledgeResponse(ackUri, null);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving order response");
                break;
            }
        }
    }

    private async Task SaveOrderResponse(XDocument doc, string? orderNumber, string? ackUri)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var safeOrderNumber = SanitizeFileName(orderNumber);
        var fileName = $"OrderResponse-{safeOrderNumber}-{timestamp}.xml";
        await File.WriteAllTextAsync(Path.Combine(_config.Folders.OrderResponses, fileName), doc.ToString());
        _logger.LogInformation("✓ Saved order response: {FileName}", fileName);
        await AcknowledgeResponse(ackUri, orderNumber);
    }

    private async Task SaveStatusMessage(XDocument doc, string? ackUri)
    {
        var orderNumber = doc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "OrderID")?.Value;
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var safeOrderNumber = SanitizeFileName(orderNumber);
        var fileName = safeOrderNumber != null
            ? $"StatusMessage-{safeOrderNumber}-{timestamp}.xml"
            : $"StatusMessage-{timestamp}.xml";
        await File.WriteAllTextAsync(Path.Combine(_config.Folders.OrderResponses, fileName), doc.ToString());
        _logger.LogInformation("✓ Saved status message: {FileName}", fileName);
        await AcknowledgeResponse(ackUri, orderNumber);
    }

    private async Task SaveAttachment(XDocument doc, string? ackUri)
    {
        var attachmentUri = doc.Root?.Attribute("Jakamo-Attachment-URI")?.Value;
        var originalFileName = doc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "Filename")?.Value;
        var orderNumber = doc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "OrderID")?.Value;

        if (string.IsNullOrEmpty(attachmentUri) || string.IsNullOrEmpty(originalFileName))
        {
            _logger.LogError("Attachment XML is missing URI or Filename");
            await AcknowledgeResponse(ackUri, null);
            return;
        }

        var token = await _tokenProvider.GetAccessTokenAsync();
        var httpClient = _httpClientFactory.CreateClient(string.Empty);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await httpClient.GetAsync(attachmentUri);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to download attachment. Status: {StatusCode}", response.StatusCode);
            await AcknowledgeResponse(ackUri, orderNumber);
            return;
        }

        var safeFileName = Path.GetFileName(originalFileName);
        var filePath = Path.Combine(_config.Folders.OrderResponses, safeFileName);
        using var fileStream = File.Create(filePath);
        await response.Content.CopyToAsync(fileStream);

        _logger.LogInformation("✓ Saved attachment: {FileName}", safeFileName);
        await AcknowledgeResponse(ackUri, orderNumber);
    }

    private async Task AcknowledgeResponse(string? ackUri, string? orderNumber)
    {
        if (string.IsNullOrEmpty(ackUri)) return;

        var ackResult = await _client.RemoveOrderResponseFromQueue(ackUri);
        if (ackResult.IsSuccess)
            _logger.LogInformation("✓ Acknowledged response for order: {OrderNumber}", orderNumber ?? "unknown");
        else
            _logger.LogWarning("Failed to acknowledge response: {Errors}", string.Join(", ", ackResult.Errors));
    }

    private MessageType DetectMessageType(string filePath)
    {
        var doc = XDocument.Load(filePath);
        var rootElement = doc.Root?.Name.LocalName;

        return rootElement switch
        {
            "Order" => MessageType.NewOrder,
            "OrderChange" => MessageType.OrderUpdate,
            "StatusMessage" => MessageType.StatusMessage,
            _ => throw new InvalidOperationException(
                $"Unknown root element: {rootElement}")
        };
    }

    private string? ExtractOrderId(string filePath)
    {
        var doc = XDocument.Load(filePath);
        
        // Use xpath to extract the order ID
        var orderIdElement = doc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName is "ID" or "OrderID" );

        return orderIdElement != null ? orderIdElement.Value : null;
    }

    private void MoveToProcessed(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var destPath = Path.Combine(_config.Folders.ProcessedOrders, fileName);
        
        // Add timestamp to avoid overwrites
        if (File.Exists(destPath))
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            fileName = $"{nameWithoutExt}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
            destPath = Path.Combine(_config.Folders.ProcessedOrders, fileName);
        }

        File.Move(filePath, destPath, true);
    }

    private void MoveToFailed(string filePath, string? errorMessage = null)
    {
        var fileName = Path.GetFileName(filePath);
        var destPath = Path.Combine(_config.Folders.FailedOrders, fileName);

        // Add timestamp to avoid overwrites
        if (File.Exists(destPath))
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            fileName = $"{nameWithoutExt}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
            destPath = Path.Combine(_config.Folders.FailedOrders, fileName);
        }

        File.Move(filePath, destPath, true);

        if (errorMessage != null)
        {
            var errorFilePath = Path.Combine(_config.Folders.FailedOrders, fileName + ".error.txt");
            File.WriteAllText(errorFilePath, FormatErrorMessage(errorMessage));
        }
    }

    private static string FormatErrorMessage(string error)
    {
        // Error format is "HTTP NNN (Reason): <body>" — try to pretty-print the body if it is XML
        var separatorIndex = error.IndexOf(": ");
        var body = separatorIndex >= 0 ? error[(separatorIndex + 2)..] : error;
        var prefix = separatorIndex >= 0 ? error[..(separatorIndex + 2)] : string.Empty;

        try
        {
            var doc = XDocument.Parse(body);
            return prefix + doc.ToString();
        }
        catch
        {
            return error;
        }
    }

    private void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(_config.Folders.InboundOrders);
        Directory.CreateDirectory(Path.Combine(_config.Folders.InboundOrders, AttachmentsSubfolder));
        Directory.CreateDirectory(_config.Folders.ProcessedOrders);
        Directory.CreateDirectory(_config.Folders.FailedOrders);
        Directory.CreateDirectory(_config.Folders.OrderResponses);

        if (_config.Logging?.EnableFileLogging == true)
        {
            var logDir = Path.GetDirectoryName(_config.Logging.LogFilePath);
            if (!string.IsNullOrEmpty(logDir))
            {
                Directory.CreateDirectory(logDir);
            }
        }
    }
}






