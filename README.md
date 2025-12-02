# Jakamo Connector Installation Guide

## Prerequisites

- Linux system with systemd
- Any linux distribution should work
- Root/sudo access
- Jakamo API Oauth2 credentials

## Installation

1. Download installation package from
```bash
wget https://github.com/JakamoLtd/jakamo-connector/releases/download/1.0.0/jakamo-connector-1.0.0.tar.gz
```

2. Extract the installation package
```bash
tar -xvzf .\jakamo-connector-1.0.0.tar.gz
```

4. Navigate to the directory
```bash
cd .\jakamo-connector-1.0.0
```

5. Run the installation script:
```bash
sudo ./install.sh
```

6. During configuration, choose either demo or production and enter your OAuth2 credentials.
   Also enter root data folder for the files to send and receive from Jakamo (default /var/lib/jakamo)
```bash
sudo nano /etc/jakamo-connector/appsettings.json
```

7. After installing re-login to have permissions applied.
   Now you can drop files in to_jakamo folder of your data folder (default is /var/lib/to_jakamo).


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

Restart service:
```bash
sudo systemctl restart jakamo-connector
```

Edit configuration:
```bash
sudo nano /etc/jakamo-connector/jakamo-connector.conf
```
