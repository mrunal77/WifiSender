# WiFi/LAN File Transfer Application Specification

## 1. Project Overview
- **Project name**: WifiSender
- **Type**: Desktop GUI Application
- **Core functionality**: Send and receive files over local network (WiFi/LAN) with a user-friendly interface
- **Target users**: Users who need to quickly transfer files between devices on the same network

## 2. UI/UX Specification

### Layout Structure
- **Window**: Single main window (800x600 minimum)
- **Layout**: Vertical split into sections
  - Header: App title and connection status
  - Mode selection: Send/Receive toggle
  - Connection panel: IP display (receive mode) / IP input (send mode)
  - File selection: File picker with file list
  - Action buttons: Send/Receive/Start Server
  - Progress area: Transfer progress bar and status

### Visual Design
- **Color palette**:
  - Primary: #2563EB (Blue)
  - Secondary: #1E293B (Dark slate)
  - Accent: #10B981 (Green for success)
  - Background: #F8FAFC (Light gray)
  - Error: #EF4444 (Red)
- **Typography**: System default (Segoe UI on Windows, SF Pro on Mac, Ubuntu on Linux)
- **Spacing**: 16px padding, 8px gaps between elements
- **Visual effects**: Rounded corners (8px), subtle shadows on cards

### Components
- **Mode Toggle**: Two buttons (Send / Receive) - active state highlighted
- **IP Display**: Label showing local IP address (auto-detected)
- **Port Input**: Default port 5555, editable
- **File Selector**: Button + list showing selected files
- **Progress Bar**: Horizontal bar showing transfer progress
- **Status Label**: Shows connection/transfer status messages

## 3. Functionality Specification

### Core Features
1. **Auto-detect local IP**: On startup, detect and display local IP address
2. **Send Mode**:
   - User enters recipient IP and port
   - User selects one or more files
   - Click "Send" to initiate transfer
   - Show progress during transfer
3. **Receive Mode**:
   - Click "Start Receiving" to listen for incoming connections
   - Display listening IP:port
   - Show progress when file is incoming
   - Save received files to user-selected directory (or Downloads)
4. **File Transfer Protocol**:
   - TCP socket-based
   - Send file metadata first (filename, size)
   - Then send file data in chunks
   - Support multiple files sequentially

### User Interactions
- Click mode buttons to switch between Send/Receive
- Click "Select Files" to open file picker
- Click "Send" to start transfer (in Send mode)
- Click "Start Receiving" to begin listening (in Receive mode)
- Click "Select Download Folder" to choose save location

### Edge Cases
- Invalid IP address format
- Connection refused / timeout
- File transfer interrupted
- No network interface found
- Port already in use

## 4. Acceptance Criteria
- App launches without errors
- Local IP is displayed correctly
- Can switch between Send and Receive modes
- Can select files for sending
- Can start receiver and show listening status
- Progress bar updates during transfer
- Status messages show current state
- Error messages display for failed operations
