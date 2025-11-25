#!/bin/bash

set -e

SERVICE_NAME="jakamo-connector"
SERVICE_USER="jakamo"
INSTALL_DIR="/opt/jakamo-connector"
SERVICE_FILE="/etc/systemd/system/${SERVICE_NAME}.service"
CONFIG_DIR="/etc/jakamo-connector"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
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

check_root() {
    if [ "$EUID" -ne 0 ]; then
        print_error "This script must be run as root (use sudo)"
        exit 1
    fi
}

confirm_uninstall() {
    echo ""
    print_warning "This will remove the Jakamo Connector service and all its files."
    read -p "Are you sure you want to continue? (yes/no): " -r
    echo ""
    if [[ ! $REPLY =~ ^[Yy][Ee][Ss]$ ]]; then
        print_info "Uninstallation cancelled"
        exit 0
    fi
}

remove_service() {
    print_info "Stopping and disabling service..."
    
    if systemctl is-active --quiet ${SERVICE_NAME}; then
        systemctl stop ${SERVICE_NAME}
    fi
    
    if systemctl is-enabled --quiet ${SERVICE_NAME} 2>/dev/null; then
        systemctl disable ${SERVICE_NAME}
    fi
    
    if [ -f "${SERVICE_FILE}" ]; then
        rm ${SERVICE_FILE}
        systemctl daemon-reload
    fi
}

remove_files() {
    print_info "Removing installation files..."
    
    if [ -d "${INSTALL_DIR}" ]; then
        rm -rf ${INSTALL_DIR}
    fi
}

remove_config() {
    read -p "Remove configuration files from ${CONFIG_DIR}? (yes/no): " -r
    echo ""
    if [[ $REPLY =~ ^[Yy][Ee][Ss]$ ]]; then
        if [ -d "${CONFIG_DIR}" ]; then
            rm -rf ${CONFIG_DIR}
            print_info "Configuration files removed"
        fi
    else
        print_info "Configuration files kept in ${CONFIG_DIR}"
    fi
}

remove_user() {
    if id "${SERVICE_USER}" &>/dev/null; then
        print_info "Removing service user: ${SERVICE_USER}"
        userdel ${SERVICE_USER} 2>/dev/null || true
    fi
}

main() {
    print_info "Starting Jakamo Connector uninstallation..."
    
    check_root
    confirm_uninstall
    remove_service
    remove_files
    remove_config
    remove_user
    
    print_info "============================================"
    print_info "Uninstallation completed successfully!"
    print_info "============================================"
}

main
