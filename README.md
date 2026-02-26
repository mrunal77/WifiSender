# WifiSender

A Python GUI application to send and receive files over WiFi or LAN.

## Requirements

- Python 3.7+
- tkinter (usually included with Python)

On some systems, you may need to install tkinter:
- **Ubuntu/Debian**: `sudo apt-get install python3-tk`
- **Fedora/RHEL**: `sudo dnf install python3-tkinter`
- **macOS**: Already included with Python

## Usage

```bash
python3 wifisender.py
```

## How to Use

### Sending Files
1. Open the app on both computers connected to the same network
2. On the receiving computer: Click "RECEIVE" mode, select a download folder, then click "START RECEIVING"
3. On the sending computer: Enter the receiver's IP address (shown on their screen), select files, then click "SEND"

### Notes
- Both computers must be on the same network (WiFi or LAN)
- The default port is 5555, but you can change it if needed
- Files are saved to the selected download folder (defaults to ~/Downloads)
- The app auto-detects your local IP address
