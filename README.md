# Jakamo Connector Installation Guide

## Prerequisites

- Linux system with systemd
- Root/sudo access
- For self-contained: No additional requirements
- For framework-dependent: .NET 8.0 Runtime

## Installation

1. Extract the installation package
2. Navigate to the directory
3. Run the installation script:
```bash
sudo ./install.sh
```

4. Edit the configuration file:
```bash
sudo nano /etc/jakamo-connector/appsettings.json
```

5. Restart the service:
```bash
sudo systemctl restart jakamo-connector
```

## Uninstallation
```bash
sudo ./uninstall.sh
```

## Troubleshooting

View logs:
```bash
sudo journalctl -u jakamo-connector -f
```

Check service status:
```bash
sudo systemctl status jakamo-connector
```