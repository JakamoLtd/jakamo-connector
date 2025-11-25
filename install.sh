#!/bin/bash

set -e

# Configuration
SERVICE_NAME="jakamo-connector"
SERVICE_USER="jakamo"
INSTALL_DIR="/opt/jakamo-connector"
SERVICE_FILE="/etc/systemd/system/${SERVICE_NAME}.service"
CONFIG_DIR="/etc/jakamo-connector"
CONFIG_FILE="${CONFIG_DIR}/jakamo-connector.conf"
LOG_DIR="/var/log/jakamo"
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m'

print_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

print_header() {
    echo -e "${CYAN}╔═══════════════════════════════════════════════════════════════╗${NC}"
    echo -e "${CYAN}║          Jakamo Connector - Installation Script              ║${NC}"
    echo -e "${CYAN}╚═══════════════════════════════════════════════════════════════╝${NC}"
    echo ""
}

check_root() {
    if [ "$EUID" -ne 0 ]; then
        print_error "This script must be run as root (use sudo)"
        exit 1
    fi
}

check_dependencies() {
    print_info "Checking dependencies..."
    
    if ! command -v systemctl &> /dev/null; then
        print_error "systemd is not available on this system"
        exit 1
    fi
}

stop_existing_service() {
    if systemctl is-active --quiet ${SERVICE_NAME}; then
        print_info "Stopping existing service..."
        systemctl stop ${SERVICE_NAME}
    fi
    
    if systemctl is-enabled --quiet ${SERVICE_NAME} 2>/dev/null; then
        print_info "Disabling existing service..."
        systemctl disable ${SERVICE_NAME}
    fi
}

create_user() {
    if id "${SERVICE_USER}" &>/dev/null; then
        print_info "User ${SERVICE_USER} already exists"
    else
        print_info "Creating service user: ${SERVICE_USER}"
        useradd -r -s /bin/false ${SERVICE_USER}
    fi
}

backup_config() {
    if [ -f "${CONFIG_FILE}" ]; then
        print_info "Backing up existing configuration..."
        cp ${CONFIG_FILE} ${CONFIG_FILE}.backup.$(date +%Y%m%d_%H%M%S)
    fi
}

prompt_configuration() {
    echo ""
    echo -e "${CYAN}╔═══════════════════════════════════════════════════════════════╗${NC}"
    echo -e "${CYAN}║                  Configuration Setup                         ║${NC}"
    echo -e "${CYAN}╚═══════════════════════════════════════════════════════════════╝${NC}"
    echo ""

    # Environment Selection
    echo -e "${BLUE}Select Jakamo Environment:${NC}"
    echo "  1) Demo (demo.thejakamo.com)"
    echo "  2) Production (www.thejakamo.com)"
    echo ""
    echo -n "Enter your choice [1-2]: "
    read ENV_CHOICE
    
    case $ENV_CHOICE in
        1)
            JAKAMO_ENV="demo"
            JAKAMO_BASE_URL="https://demo.thejakamo.com"
            JAKAMO_API_SCOPE="https://demo.jakamoapp.com/.default"
            JAKAMO_TENANT_ID="a22b6145-4c73-4914-901d-6bf96bcb0183"
            print_info "Selected: Demo environment"
            ;;
        2)
            JAKAMO_ENV="production"
            JAKAMO_BASE_URL="https://www.thejakamo.com"
            JAKAMO_API_SCOPE="https://api.jakamoapp.com/.default"
            JAKAMO_TENANT_ID="c87ad7a3-cc9b-4a04-be72-916199870e5c"
            print_info "Selected: Production environment"
            ;;
        *)
            print_error "Invalid choice. Please run the installer again."
            exit 1
            ;;
    esac

    echo ""

    # Client ID
    echo -e -n "${BLUE}Enter your OAuth2 Client ID${NC}: "
    read JAKAMO_CLIENT_ID
    while [ -z "$JAKAMO_CLIENT_ID" ]; do
        print_error "Client ID cannot be empty"
        echo -e -n "${BLUE}Enter your OAuth2 Client ID${NC}: "
        read JAKAMO_CLIENT_ID
    done

    # Client Secret
    echo -e -n "${BLUE}Enter your OAuth2 Client Secret${NC} (hidden): "
    read -s JAKAMO_CLIENT_SECRET
    echo ""
    while [ -z "$JAKAMO_CLIENT_SECRET" ]; do
        print_error "Client Secret cannot be empty"
        echo -e -n "${BLUE}Enter your OAuth2 Client Secret${NC} (hidden): "
        read -s JAKAMO_CLIENT_SECRET
        echo ""
    done

    echo ""

    # Data folder location
    echo -e "${BLUE}Data Folder Configuration:${NC}"
    echo -e -n "Enter root folder for data files [/var/lib/jakamo]: "
    read DATA_DIR_INPUT
    DATA_DIR=${DATA_DIR_INPUT:-/var/lib/jakamo}

    echo ""
    echo -e "${GREEN}Configuration Summary:${NC}"
    echo "  Environment: ${JAKAMO_ENV}"
    echo "  Base URL: ${JAKAMO_BASE_URL}"
    echo "  Tenant ID: ${JAKAMO_TENANT_ID}"
    echo "  API Scope: ${JAKAMO_API_SCOPE}"
    echo "  Client ID: ${JAKAMO_CLIENT_ID}"
    echo "  Client Secret: ****"
    echo "  Data Folder: ${DATA_DIR}"
    echo ""
    echo "Data folders will be created:"
    echo "  - ${DATA_DIR}/to_jakamo (outgoing orders)"
    echo "  - ${DATA_DIR}/from_jakamo (incoming responses)"
    echo "  - ${DATA_DIR}/processed (successfully processed)"
    echo "  - ${DATA_DIR}/failed (failed orders)"
    echo ""
    echo -n "Is this correct? (yes/no): "
    read CONFIRM
    
    if [[ ! $CONFIRM =~ ^[Yy][Ee][Ss]$ ]]; then
        echo ""
        print_warning "Installation cancelled. Please run the script again."
        exit 0
    fi
}

install_files() {
    print_info "Installing application files..."
    
    mkdir -p ${INSTALL_DIR}
    
    print_info "Copying files to ${INSTALL_DIR}..."
    cp -r ${SCRIPT_DIR}/bin/* ${INSTALL_DIR}/
    
    print_info "Searching for executable..."
    
    # Look for the actual executable name
    if [ -f "${INSTALL_DIR}/Jakamo.Connector" ]; then
        EXECUTABLE="${INSTALL_DIR}/Jakamo.Connector"
        print_info "Found executable: ${EXECUTABLE}"
        chmod +x ${EXECUTABLE}
    else
        print_error "Could not find executable file: Jakamo.Connector"
        print_info "Contents of ${INSTALL_DIR}:"
        ls -la ${INSTALL_DIR}/ | head -20
        exit 1
    fi
    
    chown -R ${SERVICE_USER}:${SERVICE_USER} ${INSTALL_DIR}
}

create_directories() {
    print_info "Creating data directories..."
    
    # Get the actual user who ran sudo (not root)
    ACTUAL_USER=${SUDO_USER:-$USER}
    
    # Create main data directory with new folder names
    mkdir -p ${DATA_DIR}/{to_jakamo,from_jakamo,processed,failed}
    
    # Create log directory
    mkdir -p ${LOG_DIR}
    
    # Set ownership - jakamo user owns everything
    chown -R ${SERVICE_USER}:${SERVICE_USER} ${DATA_DIR}
    chown -R ${SERVICE_USER}:${SERVICE_USER} ${LOG_DIR}
    
    # Set base permissions
    chmod 755 ${DATA_DIR}
    chmod 775 ${DATA_DIR}/to_jakamo        # Group writable for placing files
    chmod 755 ${DATA_DIR}/from_jakamo      
    chmod 755 ${DATA_DIR}/processed        
    chmod 755 ${DATA_DIR}/failed           
    chmod 750 ${LOG_DIR}                   # Logs are more restricted
    
    # Add the installing user to the jakamo group for access
    if [ -n "$ACTUAL_USER" ] && [ "$ACTUAL_USER" != "root" ]; then
        print_info "Adding user '${ACTUAL_USER}' to '${SERVICE_USER}' group for file access..."
        usermod -a -G ${SERVICE_USER} ${ACTUAL_USER}
        
        print_info "Created directories with permissions:"
        print_info "  - ${DATA_DIR}/to_jakamo (775 - group writable)"
        print_info "  - ${DATA_DIR}/from_jakamo (755)"
        print_info "  - ${DATA_DIR}/processed (755)"
        print_info "  - ${DATA_DIR}/failed (755)"
        echo ""
        print_warning "User '${ACTUAL_USER}' has been added to the '${SERVICE_USER}' group."
        print_warning "You need to log out and back in (or run 'newgrp ${SERVICE_USER}') for group changes to take effect."
        echo ""
    else
        print_warning "Could not determine the installing user. Data directories created with default permissions."
        print_info "To access the data directories, add your user to the '${SERVICE_USER}' group:"
        print_info "  sudo usermod -a -G ${SERVICE_USER} YOUR_USERNAME"
        print_info "  Then log out and back in"
    fi
}

setup_configuration() {
    print_info "Setting up configuration..."
    
    mkdir -p ${CONFIG_DIR}
    
    if [ -f "${CONFIG_FILE}" ] && [ -z "$JAKAMO_BASE_URL" ]; then
        print_info "Configuration file already exists, keeping existing configuration"
        # Still need to get DATA_DIR from existing config for service creation
        DATA_DIR=$(grep "^InboundOrders=" ${CONFIG_FILE} | cut -d'=' -f2 | sed 's|/to_jakamo||')
        return
    fi
    
    if [ ! -f "${SCRIPT_DIR}/config/jakamo-connector.conf_sample" ]; then
        print_error "Configuration sample file not found!"
        exit 1
    fi
    
    print_info "Creating configuration file..."
    
    # Copy template and replace values
    cp ${SCRIPT_DIR}/config/jakamo-connector.conf_sample ${CONFIG_FILE}
    
    # Replace configuration values
    sed -i "s|BaseUrl=.*|BaseUrl=${JAKAMO_BASE_URL}|g" ${CONFIG_FILE}
    sed -i "s|TenantId=.*|TenantId=${JAKAMO_TENANT_ID}|g" ${CONFIG_FILE}
    sed -i "s|ClientId=.*|ClientId=${JAKAMO_CLIENT_ID}|g" ${CONFIG_FILE}
    sed -i "s|ClientSecret=.*|ClientSecret=${JAKAMO_CLIENT_SECRET}|g" ${CONFIG_FILE}
    sed -i "s|ApiScope=.*|ApiScope=${JAKAMO_API_SCOPE}|g" ${CONFIG_FILE}
    
    # Replace folder paths
    sed -i "s|InboundOrders=.*|InboundOrders=${DATA_DIR}/to_jakamo|g" ${CONFIG_FILE}
    sed -i "s|ProcessedOrders=.*|ProcessedOrders=${DATA_DIR}/processed|g" ${CONFIG_FILE}
    sed -i "s|FailedOrders=.*|FailedOrders=${DATA_DIR}/failed|g" ${CONFIG_FILE}
    sed -i "s|OrderResponses=.*|OrderResponses=${DATA_DIR}/from_jakamo|g" ${CONFIG_FILE}
    
    # Set proper permissions
    chown ${SERVICE_USER}:${SERVICE_USER} ${CONFIG_FILE}
    chmod 600 ${CONFIG_FILE}
    
    # Create symlink in install directory
    ln -sf ${CONFIG_FILE} ${INSTALL_DIR}/jakamo-connector.conf
    
    print_info "Configuration file created at: ${CONFIG_FILE}"
}

create_systemd_service() {
    print_info "Creating systemd service..."
    
    EXECUTABLE="${INSTALL_DIR}/Jakamo.Connector"
    
    if [ ! -f "$EXECUTABLE" ]; then
        print_error "Executable not found: ${EXECUTABLE}"
        exit 1
    fi
    
    print_info "Service will execute: ${EXECUTABLE}"
    
    cat > ${SERVICE_FILE} <<EOF
[Unit]
Description=Jakamo API Connector Service
After=network.target

[Service]
Type=notify
ExecStart=${EXECUTABLE}
WorkingDirectory=${INSTALL_DIR}

User=${SERVICE_USER}
Group=${SERVICE_USER}

Restart=on-failure
RestartSec=10

Environment=DOTNET_ENVIRONMENT=Production

StandardOutput=journal
StandardError=journal
SyslogIdentifier=${SERVICE_NAME}

NoNewPrivileges=true
PrivateTmp=true

[Install]
WantedBy=multi-user.target
EOF

    systemctl daemon-reload
}

enable_and_start_service() {
    print_info "Enabling service..."
    systemctl enable ${SERVICE_NAME}
    
    print_info "Starting service..."
    if systemctl start ${SERVICE_NAME}; then
        sleep 3
        if systemctl is-active --quiet ${SERVICE_NAME}; then
            print_info "Service started successfully"
            systemctl status ${SERVICE_NAME} --no-pager -l || true
        else
            print_error "Service failed to start"
            echo ""
            print_info "Showing last 20 log lines:"
            journalctl -u ${SERVICE_NAME} -n 20 --no-pager
            exit 1
        fi
    else
        print_error "Failed to start service"
        print_info "Check logs with: sudo journalctl -u ${SERVICE_NAME} -n 50"
        exit 1
    fi
}

show_post_install_info() {
    echo ""
    echo -e "${CYAN}╔═══════════════════════════════════════════════════════════════╗${NC}"
    echo -e "${CYAN}║          Installation Completed Successfully!                ║${NC}"
    echo -e "${CYAN}╚═══════════════════════════════════════════════════════════════╝${NC}"
    echo ""
    echo -e "${GREEN}Service Management:${NC}"
    echo "  sudo systemctl status ${SERVICE_NAME}    # Check service status"
    echo "  sudo systemctl stop ${SERVICE_NAME}      # Stop the service"
    echo "  sudo systemctl start ${SERVICE_NAME}     # Start the service"
    echo "  sudo systemctl restart ${SERVICE_NAME}   # Restart the service"
    echo ""
    echo -e "${GREEN}View Logs:${NC}"
    echo "  sudo journalctl -u ${SERVICE_NAME} -f        # Follow logs in real-time"
    echo "  sudo journalctl -u ${SERVICE_NAME} -n 100    # Last 100 log entries"
    echo "  sudo tail -f ${LOG_DIR}/connector.log        # Follow file log"
    echo ""
    echo -e "${GREEN}Configuration:${NC}"
    echo "  Config file: ${CONFIG_FILE}"
    echo "  Edit with:   sudo nano ${CONFIG_FILE}"
    echo "  After edit:  sudo systemctl restart ${SERVICE_NAME}"
    echo ""
    echo -e "${GREEN}Data Directories:${NC}"
    echo "  To Jakamo:   ${DATA_DIR}/to_jakamo"
    echo "  From Jakamo: ${DATA_DIR}/from_jakamo"
    echo "  Processed:   ${DATA_DIR}/processed"
    echo "  Failed:      ${DATA_DIR}/failed"
    echo ""
    echo -e "${YELLOW}To send orders to Jakamo, place XML files in:${NC}"
    echo "  ${DATA_DIR}/to_jakamo"
    echo ""
}

main() {
    print_header
    
    check_root
    check_dependencies
    
    # Check if this is a fresh install or upgrade
    if [ -f "${CONFIG_FILE}" ]; then
        print_info "Existing installation detected - upgrading..."
        stop_existing_service
        create_user
        backup_config
        install_files
        # Get DATA_DIR from existing config
        DATA_DIR=$(grep "^InboundOrders=" ${CONFIG_FILE} 2>/dev/null | cut -d'=' -f2 | sed 's|/to_jakamo||' || echo "/var/lib/jakamo")
        create_directories
        # Skip configuration prompt on upgrade
        setup_configuration
    else
        print_info "Fresh installation detected"
        stop_existing_service
        create_user
        prompt_configuration
        install_files
        create_directories
        setup_configuration
    fi
    
    create_systemd_service
    enable_and_start_service
    show_post_install_info
}

main