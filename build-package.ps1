param(
    [string]$Version = "1.0.0",
    [string]$ProjectPath = ".\src\Jakamo.Connector\Jakamo.Connector.csproj",
    [string]$ConfigTemplate = ".\src\Jakamo.Connector\jakamo-connector.conf_sample"
)

$ErrorActionPreference = "Stop"

# Configuration
$PackageName = "jakamo-connector-$Version"
$BuildDir = ".\build"
$DistDir = "$BuildDir\$PackageName"

function Write-Info {
    param([string]$Message)
    Write-Host "[INFO] $Message" -ForegroundColor Green
}

function Write-Warning-Custom {
    param([string]$Message)
    Write-Host "[WARN] $Message" -ForegroundColor Yellow
}

# Clean previous build
Write-Info "Cleaning previous build..."
if (Test-Path $BuildDir) {
    Remove-Item -Path $BuildDir -Recurse -Force
}
New-Item -ItemType Directory -Path $DistDir -Force | Out-Null

# Publish the application (self-contained for linux-x64)
Write-Info "Publishing application..."
dotnet publish $ProjectPath `
    -c Release `
    -r linux-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -o "$DistDir\bin"

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed!"
    exit 1
}

# Create config directory with template
Write-Info "Creating configuration template..."
New-Item -ItemType Directory -Path "$DistDir\config" -Force | Out-Null

# Copy the config template
if (Test-Path $ConfigTemplate) {
    Write-Info "Copying jakamo-connector.conf_sample..."
    Copy-Item $ConfigTemplate -Destination "$DistDir\config\jakamo-connector.conf_sample"
} else {
    Write-Error "Configuration template not found: $ConfigTemplate"
    exit 1
}

# Copy install and uninstall scripts
Write-Info "Copying installation scripts..."
if (Test-Path ".\install.sh") {
    Copy-Item ".\install.sh" -Destination "$DistDir\install.sh"
} else {
    Write-Warning-Custom "install.sh not found in current directory"
}

if (Test-Path ".\uninstall.sh") {
    Copy-Item ".\uninstall.sh" -Destination "$DistDir\uninstall.sh"
} else {
    Write-Warning-Custom "uninstall.sh not found in current directory"
}

# Create README.md
Write-Info "Creating README..."
$readmeContent = @"
# Jakamo Connector Installation Guide

Version: $Version

## Prerequisites

- Linux system with systemd
- Root/sudo access
- Self-contained deployment (no .NET runtime required)

## Installation

1. Extract the installation package:
``````bash
tar -xzf jakamo-connector-$Version.tar.gz
cd jakamo-connector-$Version
``````

2. Run the installation script:
``````bash
sudo ./install.sh
``````

3. Edit the configuration file:
``````bash
sudo nano /etc/jakamo-connector/jakamo-connector.conf
``````

4. Configure your Jakamo API credentials:
   - **ClientId**: Your OAuth2 client ID (provided by Jakamo)
   - **ClientSecret**: Your OAuth2 client secret (provided by Jakamo)
   - **BaseUrl**: Jakamo API endpoint
   - **TokenEndpoint**: OAuth2 token endpoint

5. The installer automatically creates required folders:
   - ``/var/lib/jakamo/inbound`` - Place XML files here for processing
   - ``/var/lib/jakamo/processed`` - Successfully processed files
   - ``/var/lib/jakamo/failed`` - Failed files for review
   - ``/var/lib/jakamo/responses`` - Order responses from Jakamo

6. Restart the service to apply configuration:
``````bash
sudo systemctl restart jakamo-connector
``````

## Configuration File

The configuration file is located at: ``/etc/jakamo-connector/jakamo-connector.conf``

### API Configuration
- ``BaseUrl``: Your Jakamo API endpoint
- ``TokenEndpoint``: OAuth2 token endpoint
- ``ClientId``: Your OAuth2 client ID
- ``ClientSecret``: Your OAuth2 client secret

### Folder Configuration
All folders are created automatically during installation with proper permissions.

### Polling Configuration
- ``InboundCheckInterval``: How often to check for new files (in seconds)
- ``ResponseCheckInterval``: How often to check for responses (in seconds)
- ``MaxRetryAttempts``: Maximum retry attempts for failed operations

### Logging Configuration
- ``EnableFileLogging``: Enable/disable file logging (true/false)
- ``LogFile``: Log file location (default: /var/log/jakamo/connector.log)
- ``LogLevel``: Debug, Information, Warning, or Error

## Usage

### Sending Orders to Jakamo
1. Place your XML order files in ``/var/lib/jakamo/inbound``
2. The connector automatically processes them
3. Successfully processed files move to ``/var/lib/jakamo/processed``
4. Failed files move to ``/var/lib/jakamo/failed``

### Receiving Order Responses
Order responses from Jakamo are automatically saved to ``/var/lib/jakamo/responses``

## Service Management

Check service status:
``````bash
sudo systemctl status jakamo-connector
``````

Stop the service:
``````bash
sudo systemctl stop jakamo-connector
``````

Start the service:
``````bash
sudo systemctl start jakamo-connector
``````

Restart the service:
``````bash
sudo systemctl restart jakamo-connector
``````

## Viewing Logs

The connector logs to both systemd journal and a file (if enabled).

Follow systemd logs in real-time:
``````bash
sudo journalctl -u jakamo-connector -f
``````

View last 100 systemd log entries:
``````bash
sudo journalctl -u jakamo-connector -n 100
``````

View file logs (if enabled):
``````bash
sudo tail -f /var/log/jakamo/connector.log
``````

## Troubleshooting

### Service won't start
1. Check the configuration file:
``````bash
   sudo cat /etc/jakamo-connector/jakamo-connector.conf
``````

2. Verify API credentials are correct

3. Check service logs:
``````bash
   sudo journalctl -u jakamo-connector -n 50
``````

### Files not being processed
1. Verify files are in the correct folder: ``/var/lib/jakamo/inbound``
2. Check folder permissions (should be owned by jakamo user)
3. Review logs for error messages

### Connection issues
1. Verify BaseUrl and TokenEndpoint are correct
2. Test network connectivity to the API endpoint
3. Verify ClientId and ClientSecret are valid

## Uninstallation
``````bash
sudo ./uninstall.sh
``````

This will prompt you to confirm removal and optionally keep:
- Configuration files in ``/etc/jakamo-connector/``
- Data folders in ``/var/lib/jakamo/``
- Log files in ``/var/log/jakamo/``

## Support

For issues or questions, please contact Jakamo support.
"@

$readmeContent | Out-File -FilePath "$DistDir\README.md" -Encoding UTF8

# Create VERSION file
Write-Info "Creating VERSION file..."
$Version | Out-File -FilePath "$DistDir\VERSION" -Encoding UTF8 -NoNewline

# Create tar.gz archive
Write-Info "Creating distribution package..."
$archivePath = "$BuildDir\$PackageName.tar.gz"

# Try to use WSL tar if available
try {
    $wslAvailable = $false
    try {
        wsl --list --quiet 2>$null | Out-Null
        $wslAvailable = $true
    } catch {}

    if ($wslAvailable) {
        Write-Info "Using WSL tar..."
        $windowsPath = Resolve-Path $BuildDir
        $wslPath = wsl wslpath "'$windowsPath'"
        wsl tar -czf "$wslPath/$PackageName.tar.gz" -C "$wslPath" "$PackageName"
        Write-Info "Package created: $archivePath"
    } else {
        # Fall back to zip if tar is not available
        Write-Warning-Custom "WSL not available, creating ZIP archive instead..."
        $zipPath = "$BuildDir\$PackageName.zip"
        Compress-Archive -Path $DistDir -DestinationPath $zipPath -Force
        Write-Info "Package created: $zipPath"
        Write-Warning-Custom "Note: Created ZIP instead of tar.gz. Linux users can extract with 'unzip' command."
    }
} catch {
    Write-Warning-Custom "Could not create tar.gz, falling back to ZIP..."
    $zipPath = "$BuildDir\$PackageName.zip"
    Compress-Archive -Path $DistDir -DestinationPath $zipPath -Force
    Write-Info "Package created: $zipPath"
}

# Summary
Write-Info ""
Write-Info "============================================"
Write-Info "Build completed successfully!"
Write-Info "============================================"
Write-Info ""
Write-Info "Package contents:"
Write-Info "  - Application binaries in 'bin/'"
Write-Info "  - Configuration template in 'config/jakamo-connector.conf_sample'"
Write-Info "  - Installation scripts (install.sh, uninstall.sh)"
Write-Info "  - README.md with installation instructions"
Write-Info ""
Write-Info "Distribution package: $BuildDir\$PackageName.*"
Write-Info ""
Write-Info "To test locally with WSL/Linux:"
Write-Info "  tar -xzf $PackageName.tar.gz  (or unzip for .zip)"
Write-Info "  cd $PackageName"
Write-Info "  sudo ./install.sh"
Write-Info ""
Write-Info "Configuration:"
Write-Info "  Sample: config/jakamo-connector.conf_sample"
Write-Info "  Will be installed to: /etc/jakamo-connector/jakamo-connector.conf"
Write-Info ""