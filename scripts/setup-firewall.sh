#!/usr/bin/env bash
set -euo pipefail

DISCOVERY_PORT=5556
TRANSFER_PORT=5555

detect_firewall() {
    if command -v ufw &>/dev/null; then
        if ufw status | grep -q "Status: active"; then
            echo "ufw"
            return
        fi
    fi

    if command -v firewall-cmd &>/dev/null; then
        if firewall-cmd --state &>/dev/null; then
            echo "firewalld"
            return
        fi
    fi

    if command -v iptables &>/dev/null; then
        local policy
        policy=$(iptables -L INPUT --line-numbers -n 2>/dev/null | head -1)
        if echo "$policy" | grep -qE "policy (DROP|REJECT)"; then
            echo "iptables"
            return
        fi
        if iptables -L INPUT -n 2>/dev/null | grep -qE "(DROP|REJECT)"; then
            echo "iptables"
            return
        fi
    fi

    if command -v nft &>/dev/null; then
        if nft list ruleset 2>/dev/null | grep -q "hook input"; then
            echo "nftables"
            return
        fi
    fi

    echo "none"
}

setup_ufw() {
    echo "  Adding ufw rule for UDP ${DISCOVERY_PORT}..."
    ufw allow "$DISCOVERY_PORT/udp" comment 'WifiSender discovery' 2>/dev/null || true
    echo "  Adding ufw rule for TCP ${TRANSFER_PORT}..."
    ufw allow "$TRANSFER_PORT/tcp" comment 'WifiSender transfer' 2>/dev/null || true
    ufw reload 2>/dev/null || true
}

setup_firewalld() {
    echo "  Adding firewalld rule for UDP ${DISCOVERY_PORT}..."
    firewall-cmd --add-port="$DISCOVERY_PORT/udp" 2>/dev/null || true
    echo "  Adding firewalld rule for TCP ${TRANSFER_PORT}..."
    firewall-cmd --add-port="$TRANSFER_PORT/tcp" 2>/dev/null || true
    echo "  Making rules permanent..."
    firewall-cmd --runtime-to-permanent 2>/dev/null || true
}

setup_iptables() {
    echo "  Adding iptables rule for UDP ${DISCOVERY_PORT}..."
    iptables -C INPUT -p udp --dport "$DISCOVERY_PORT" -j ACCEPT 2>/dev/null || \
        iptables -A INPUT -p udp --dport "$DISCOVERY_PORT" -j ACCEPT
    echo "  Adding iptables rule for TCP ${TRANSFER_PORT}..."
    iptables -C INPUT -p tcp --dport "$TRANSFER_PORT" -j ACCEPT 2>/dev/null || \
        iptables -A INPUT -p tcp --dport "$TRANSFER_PORT" -j ACCEPT
}

setup_nftables() {
    echo "  Adding nftables rule for UDP ${DISCOVERY_PORT}..."
    nft add rule inet filter input udp dport "$DISCOVERY_PORT" accept 2>/dev/null || true
    echo "  Adding nftables rule for TCP ${TRANSFER_PORT}..."
    nft add rule inet filter input tcp dport "$TRANSFER_PORT" accept 2>/dev/null || true
}

main() {
    if [ "$EUID" -ne 0 ]; then
        echo "ERROR: You need to run this script with root privileges."
        echo ""
        echo "  sudo $0"
        echo ""
        echo "Or from the app, click the 'Fix Firewall' button which will"
        echo "prompt for your password via Polkit (pkexec)."
        exit 1
    fi

    local firewall
    firewall=$(detect_firewall)

    echo "Detected firewall: $firewall"

    case "$firewall" in
        ufw)
            setup_ufw
            ;;
        firewalld)
            setup_firewalld
            ;;
        iptables)
            setup_iptables
            ;;
        nftables)
            setup_nftables
            ;;
        none)
            echo "No active firewall detected. Nothing to configure."
            echo "If you have a custom firewall setup, ensure these ports are open:"
            echo "  UDP $DISCOVERY_PORT - Device discovery"
            echo "  TCP $TRANSFER_PORT - File transfer"
            exit 0
            ;;
    esac

    echo ""
    echo "WifiSender firewall configuration complete!"
    echo "  UDP $DISCOVERY_PORT - Device discovery allowed"
    echo "  TCP $TRANSFER_PORT - File transfer allowed"
}

main
