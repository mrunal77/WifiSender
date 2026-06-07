# WifiSender

A cross-platform desktop application to send and receive files over WiFi or LAN.

## Features

- **Cross-platform**: Works on Windows, Linux, and macOS
- **Auto-detect local IP**: Automatically detects your local network IP
- **Send files**: Select files and send to any device on your network
- **Receive files**: Start receiving and save files to a selected folder
- **Progress tracking**: Visual progress bar for file transfers
- **Test connection**: Verify connectivity before sending

## Requirements

- .NET 10.0 or higher

## Running the Application

### From published files:
```bash
./publish/WifiSender
```

### From source:
```bash
dotnet run
```

### Building:
```bash
# Debug build
dotnet build

# Release build
dotnet publish -c Release -o ./publish
```

## How to Use

### Sending Files
1. Open the app on both computers connected to the same network
2. On the receiving computer: Click "START RECEIVING"
3. On the sending computer: 
   - Enter the receiver's IP address
   - Click "Select Files" to choose files
   - Click "SEND"

### Notes
- Both computers must be on the same network (WiFi or LAN)
- The default port is 5555, but you can change it if needed
- Files are saved to the selected download folder (defaults to ~/Downloads)
- Use "Test" button to verify connectivity before sending

## Firewall Configuration

Device discovery uses **UDP port 5556**, and file transfers use **TCP port 5555** (configurable).
Your firewall must allow these ports for the app to work properly.

### Automatic Setup (Linux)

Run the included script with admin privileges:

```bash
sudo scripts/setup-firewall.sh
```

The script auto-detects your firewall (ufw, firewalld, iptables, or nftables) and adds the
required rules. You can also click "Fix Firewall" in the app's Send tab, which launches the
script via Polkit (pkexec).

### Manual Setup

- **ufw**: `sudo ufw allow 5556/udp && sudo ufw allow 5555/tcp`
- **firewalld**: `sudo firewall-cmd --add-port=5556/udp --add-port=5555/tcp && sudo firewall-cmd --runtime-to-permanent`
- **iptables**: `sudo iptables -A INPUT -p udp --dport 5556 -j ACCEPT && sudo iptables -A INPUT -p tcp --dport 5555 -j ACCEPT`

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
