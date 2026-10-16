# PhotoVault Scanner Bridge (v1.3)

A lightweight, zero-configuration local bridge that allows PhotoVault (hosted on GitHub Pages or locally) to scan directly from USB flatbed and document scanners **100% inside your browser with zero external apps or popups**.

---

### Features & Capabilities
* **100% In-App UI**: Zero external Windows or HP popup windows. Scans stream straight from the optical sensor into PhotoVault's in-browser Scan Studio.
* **1200 DPI Ultra High-Resolution**: Supports 200 (Draft), 300 (Standard Photo), 600 (High-Resolution), and **1200 DPI Archival RAW** sampling for master preservation.
* **Lossless TIFF RAW & JPEG**: Choose between compressed high-quality JPEG and **Lossless TIFF RAW** (`.tif`) with uncompressed optical sensor fidelity and companion fast-preview streaming.
* **Interactive Scan Studio**: Real-time auto-detect photo borders, freeform & aspect ratio cropping (4×6, 5×7, 1:1), 90° rotation, horizontal/vertical flipping, vintage contrast enhancement, tagging, and direct saving into IndexedDB / local folders.

---

### Why is this needed?
Modern web browsers (Chrome, Edge, Firefox, Safari) have strict hardware sandboxing:
1. **USB Scanners are NOT standard HID devices**: Flatbed and document scanners (HP, Canon, Epson, Brother, Fujitsu) use USB Imaging Class (`0x06`) or vendor bulk transfer endpoints, whereas `navigator.hid` (WebHID) is restricted strictly to Human Interface Devices (keyboards, gamepads, barcode wands).
2. **OS Driver Exclusivity**: Windows automatically claims USB scanners with its native WIA/TWAIN driver stack, which blocks raw browser access via WebUSB.
3. **GitHub Pages Security**: Because PhotoVault runs as a static client-side web app on GitHub Pages (`https://shamsghani.github.io/photovault/`), this tiny local bridge runs on `127.0.0.1:18205` with full CORS and Private Network Access (`Access-Control-Allow-Private-Network`) headers, enabling seamless, zero-latency communication without sending any photos or data to third-party cloud servers.
4. **Zero Out-of-Memory Overhead**: Employs streaming chunked base64 serialization, enabling smooth capture and transfer of massive 1200 DPI scans without memory bottlenecks.

---

### Quick Start (Windows)
1. Double-click `run-scanner-bridge.bat` (or run `PhotoVaultScannerBridge.exe`).
2. Keep the small console window open in the background while scanning.
3. Open PhotoVault in your browser and click **Scan Photos**.
4. Your scanner is detected automatically. Select your resolution (200, 300, 600, or **1200 DPI**) and format (**JPEG** or **TIFF Raw**).
5. Click **Scan Photo Directly**:
   - The photo opens instantly in PhotoVault's **In-App Scan Studio**.
   - **Auto-Crop Photo**: Automatically detects photo edges on the glass bed.
   - **Interactive Crop Handles**: Freeform or standard photo ratios (4×6, 5×7, 1:1, Full Bed).
   - **Rotation & Flip**: 90° CCW / CW, Horizontal and Vertical flip.
   - **Visual Adjustments**: Contrast and Brightness sliders + Auto-Enhance for vintage prints.
   - **Save to Vault**: Saves the photo (JPEG or archival TIFF `.tif`) directly to your vault and disk folder.

---

### Included Files
* **`PhotoVaultScannerBridge.exe`**: Native 64-bit Windows executable compiled with .NET Framework (27 KB, zero external dependencies, runs on any Windows 10/11 PC).
* **`PhotoVaultScannerBridge.cs`**: Full C# source code. Compiled with Windows' built-in `csc.exe` + `System.Drawing.dll`.
* **`scanner-bridge.ps1`**: Pure PowerShell alternative (runs out-of-the-box without compilation).
* **`run-scanner-bridge.bat`**: Single-click launcher.

---

### API Endpoints
* `GET /status` — Checks bridge health and returns list of connected scanners, supported DPIs (200, 300, 600, 1200), and formats (`jpeg`, `tiff`).
* `GET /devices` — Enumerates scanner device IDs and descriptions.
* `POST /scan` — Triggers silent scan with payload:
  ```json
  {
    "dpi": 1200,
    "format": "tiff",
    "colorMode": "color",
    "deviceId": "{...}"
  }
  ```
  Returns:
  ```json
  {
    "success": true,
    "width": 10200,
    "height": 13200,
    "dpi": 1200,
    "format": "tiff",
    "mime": "image/tiff",
    "size": 45182900,
    "preview": "data:image/jpeg;base64,...",
    "data": "data:image/tiff;base64,..."
  }
  ```
