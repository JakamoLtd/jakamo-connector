# Jakamo Connector Installation Guide

Version: 1.0.1

## Prerequisites

- Linux system with systemd
- Root/sudo access
- Self-contained deployment (no .NET runtime required)

## Installation

1. Extract the installation package:
```bash
tar -xzf jakamo-connector-1.0.1.tar.gz
cd jakamo-connector-1.0.1
```

2. Run the installation script:
```bash
sudo ./install.sh
```

3. Edit the configuration file:
```bash
sudo nano /etc/jakamo-connector/jakamo-connector.conf
```

4. Configure your Jakamo API credentials:
   - **ClientId**: Your OAuth2 client ID (provided by Jakamo)
   - **ClientSecret**: Your OAuth2 client secret (provided by Jakamo)
   - **BaseUrl**: Jakamo API endpoint
   - **TenantId**: Your Azure AD tenant ID (provided by Jakamo)
   - **ApiScope**: OAuth2 API scope (provided by Jakamo)

5. The installer automatically creates required folders:
   - `/var/lib/jakamo/to_jakamo` - Place XML order files here for processing
   - `/var/lib/jakamo/to_jakamo/attachments` - Place file attachments here to upload
   - `/var/lib/jakamo/processed` - Successfully processed files
   - `/var/lib/jakamo/failed` - Failed files for review
   - `/var/lib/jakamo/from_jakamo` - Order responses from Jakamo

6. Restart the service to apply configuration:
```bash
sudo systemctl restart jakamo-connector
```

## Configuration File

The configuration file is located at: `/etc/jakamo-connector/jakamo-connector.conf`

### API Configuration
- `BaseUrl`: Your Jakamo API endpoint
- `TenantId`: Your Azure AD tenant ID
- `ApiScope`: OAuth2 API scope
- `ClientId`: Your OAuth2 client ID
- `ClientSecret`: Your OAuth2 client secret

### Folder Configuration
All folders are created automatically during installation with proper permissions.

### Polling Configuration
- `InboundCheckInterval`: How often to check for new files (in seconds)
- `ResponseCheckInterval`: How often to check for responses (in seconds)
- `MaxRetryAttempts`: Maximum retry attempts for failed operations

### Logging Configuration
- `EnableFileLogging`: Enable/disable file logging (true/false)
- `LogFile`: Log file location (default: /var/log/jakamo/connector.log)
- `LogLevel`: Debug, Information, Warning, or Error

## Usage

### Sending Orders to Jakamo
1. Place your XML order files in `/var/lib/jakamo/to_jakamo`
2. The connector automatically detects and processes them
3. Successfully processed files move to `/var/lib/jakamo/processed`
4. Failed files move to `/var/lib/jakamo/failed` with an `.error.txt` file explaining the error

### Uploading File Attachments
Attachments are linked to orders by filename. The filename must start with the order number followed by an underscore:

`{OrderNumber}_{FileName}.{ext}`

For example: `PO-1234_drawing.pdf` will be uploaded as an attachment to order `PO-1234`.

1. Place the file in `/var/lib/jakamo/to_jakamo/attachments`
2. The connector uploads it to the correct order automatically
3. Successfully uploaded files move to `/var/lib/jakamo/processed`
4. Failed uploads move to `/var/lib/jakamo/failed` with an `.error.txt` file

Supported file types include PDF, XML, CSV, Excel, ZIP, PNG, JPEG, and others (unknown types are sent as `application/octet-stream`).

### Receiving Order Responses
Order responses from Jakamo are automatically saved to `/var/lib/jakamo/from_jakamo`

## Service Management

Check service status:
```bash
sudo systemctl status jakamo-connector
```

Stop the service:
```bash
sudo systemctl stop jakamo-connector
```

Start the service:
```bash
sudo systemctl start jakamo-connector
```

Restart the service:
```bash
sudo systemctl restart jakamo-connector
```

## Viewing Logs

The connector logs to both systemd journal and a file (if enabled).

Follow systemd logs in real-time:
```bash
sudo journalctl -u jakamo-connector -f
```

View last 100 systemd log entries:
```bash
sudo journalctl -u jakamo-connector -n 100
```

View file logs (if enabled):
```bash
sudo tail -f /var/log/jakamo/connector.log
```

## Troubleshooting

### Service won't start
1. Check the configuration file:
```bash
   sudo cat /etc/jakamo-connector/jakamo-connector.conf
```

2. Verify API credentials are correct

3. Check service logs:
```bash
   sudo journalctl -u jakamo-connector -n 50
```

### Files not being processed
1. Verify files are in the correct folder: `/var/lib/jakamo/to_jakamo` (or `attachments` subfolder for file attachments)
2. Check folder permissions (should be owned by jakamo user)
3. Review logs for error messages

### Connection issues
1. Verify BaseUrl, TenantId, and ApiScope are correct
2. Test network connectivity to the API endpoint
3. Verify ClientId and ClientSecret are valid

## Uninstallation
```bash
sudo ./uninstall.sh
```

This will prompt you to confirm removal and optionally keep:
- Configuration files in `/etc/jakamo-connector/`
- Data folders in `/var/lib/jakamo/`
- Log files in `/var/log/jakamo/`

## Support

For issues or questions, please contact Jakamo support.
