# Jakamo Connector Installation Guide

Version: 1.0.0

## Prerequisites

- Linux system with systemd
- Root/sudo access
- Self-contained deployment (no .NET runtime required)

## Installation

1. Extract the installation package:
```bash
tar -xzf jakamo-connector-1.0.0.tar.gz
cd jakamo-connector-1.0.0
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
   - **TokenEndpoint**: OAuth2 token endpoint

5. The installer automatically creates required folders:
   - `/var/lib/jakamo/inbound` - Place XML files here for processing
   - `/var/lib/jakamo/processed` - Successfully processed files
   - `/var/lib/jakamo/failed` - Failed files for review
   - `/var/lib/jakamo/responses` - Order responses from Jakamo

6. Restart the service to apply configuration:
```bash
sudo systemctl restart jakamo-connector
```

## Configuration File

The configuration file is located at: `/etc/jakamo-connector/jakamo-connector.conf`

### API Configuration
- `BaseUrl`: Your Jakamo API endpoint
- `TokenEndpoint`: OAuth2 token endpoint
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
1. Place your XML order files in `/var/lib/jakamo/inbound`
2. The connector automatically processes them
3. Successfully processed files move to `/var/lib/jakamo/processed`
4. Failed files move to `/var/lib/jakamo/failed`

### Receiving Order Responses
Order responses from Jakamo are automatically saved to `/var/lib/jakamo/responses`

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
1. Verify files are in the correct folder: `/var/lib/jakamo/inbound`
2. Check folder permissions (should be owned by jakamo user)
3. Review logs for error messages

### Connection issues
1. Verify BaseUrl and TokenEndpoint are correct
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
