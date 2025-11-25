using System.Xml.Linq;
using Ardalis.Result;
using Jakamo.Api.Connector.Service.Config;
using Jakamo.Api.Interfaces;

namespace Jakamo.Api.Connector.Service;
public class JakamoConnectorService : BackgroundService
{
    private readonly ILogger<JakamoConnectorService> _logger;
    private readonly IPurchaseOrderClient _client;
    private readonly ConnectorConfig _config;

    public JakamoConnectorService(
        ILoggerFactory loggerFactory,
        ConnectorConfig config,
        IPurchaseOrderClient client)
    {
        _logger = loggerFactory.CreateLogger<JakamoConnectorService>();
        
        _config = config;
        _client = client;
        
        // Ensure directories exist
        EnsureDirectoriesExist();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Jakamo Connector Service started");
        _logger.LogInformation("Monitoring folder: {Folder}", _config.Folders.InboundOrders);
        _logger.LogInformation("Response folder: {Folder}", _config.Folders.OrderResponses);

        // Start both polling tasks
        var inboundTask = PollInboundOrders(stoppingToken);
        var responseTask = PollOrderResponses(stoppingToken);

        await Task.WhenAll(inboundTask, responseTask);
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
            bool success = await SendMessage(filePath, messageType, orderId);

            if (success)
            {
                MoveToProcessed(filePath);
                _logger.LogInformation("✓ Successfully processed: {FileName}", fileName);
            }
            else
            {
                MoveToFailed(filePath);
                _logger.LogWarning("✗ Failed to process: {FileName}", fileName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing {FileName}", fileName);
            MoveToFailed(filePath);
        }
    }

    private async Task<bool> SendMessage(string filePath, MessageType messageType, string? orderId)
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
            _logger.LogError("API Error: {Errors}", string.Join(", ", result.Errors));
        }

        return result.IsSuccess;
    }

    private async Task ProcessOrderResponses()
    {
        while (true)
        {
            var result = await _client.GetOrderResponse();

            if (result.Status == ResultStatus.NotFound)
            {
                // No more responses available
                break;
            }

            if (!result.IsSuccess)
            {
                _logger.LogError("Failed to fetch order response: {Errors}",
                    string.Join(", ", result.Errors));
                break;
            }

            try
            {
                // Load and save the response
                var responseStream = result.Value.XmlStream;
                var doc = XDocument.Load(responseStream);
                
                // Generate filename from order number or timestamp
                var orderNumber = result.Value.OrderNumber;
                var fileName = $"{orderNumber}.xml";
                var filePath = Path.Combine(_config.Folders.OrderResponses, fileName);

                await File.WriteAllTextAsync(filePath, doc.ToString());
                _logger.LogInformation("✓ Saved order response: {FileName}", fileName);

                // Acknowledge the response (remove from queue)
                var ackUri = result.Value.AcknowledgementUri;
                if (!string.IsNullOrEmpty(ackUri))
                {
                    var ackResult = await _client.RemoveOrderResponseFromQueue(ackUri);
                    if (ackResult.IsSuccess)
                    {
                        _logger.LogInformation("✓ Acknowledged response for order: {OrderNumber}",
                            orderNumber);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to acknowledge response: {Errors}",
                            string.Join(", ", ackResult.Errors));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving order response");
                break;
            }
        }
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

    private void MoveToFailed(string filePath)
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
    }

    private void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(_config.Folders.InboundOrders);
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






