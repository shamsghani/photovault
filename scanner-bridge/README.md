# PhotoVault Scanner Bridge (v1.4)

A lightweight, zero-configuration local bridge that allows PhotoVault (hosted on GitHub Pages or locally) to scan directly from USB flatbed and document scanners **100% inside your browser with zero external apps or popups**.

---

### Features & Capabilities
* **100% In-App UI**: Zero external Windows or HP popup windows. Scans stream straight from the optical sensor into PhotoVault's in-browser Scan Studio.
* **2-Step Smart Workflow**:
  1. **Fast 75 DPI Flatbed Preview**: Captures the entire flatbed glass in ~2 seconds at low resolution.
  2. **Automatic Computer Vision Photo Crop**: Automatically detects the photo borders on the glass bed and snaps the crop box around it.
  3. **High-Resolution Cropped Hardware Scan**: The scanner carriage moves *only* over the cropped photo area, scanning it directly at **1200 DPI TIFF RAW** (or 600/300 DPI JPEG).
* **1200 DPI Ultra High-Resolution**: Supports 75 (Preview), 100, 200, 300, 600, and **1200 DPI Archival RAW** sampling for master preservation.
* **Lossless TIFF RAW & JPEG**: Choose between compressed JPEG and **Lossless TIFF RAW** (`.tif`) with uncompressed optical sensor fidelity and companion fast-preview streaming.
* **Interactive Scan Studio**: Real-time auto-detect photo borders, freeform & aspect ratio cropping (4×6, 5×7, 1:1), 90° rotation, horizontal/vertical flipping, vintage contrast enhancement, tagging, and direct saving into IndexedDB / local folders.

---

### Quick Start (Windows)
1. Double-click `run-scanner-bridge.bat` (or run `PhotoVaultScannerBridge.exe`).
2. Keep the small console window open in the background while scanning.
3. Open PhotoVault in your browser and click **Scan Photos**.
4. Select your target resolution (e.g. **1200 DPI**) and format (**TIFF Raw**).
5. Click **⚡ 1. Preview Scan & Auto-Crop**:
   - Takes ~2 seconds to capture the glass bed overview.
   - Automatically detects the photo edges on the glass bed.
   - Fine-tune crop handles or pick a standard ratio if needed.
6. Click **🎯 2. Scan Cropped Area (1200 DPI TIFF RAW)**:
   - The scanner optical sensor carriage scans only the cropped photo at true 1200 DPI.
   - The lossless optical master opens in the studio.
7. Click **Save to Vault** or **Download .tif**!

---

### Included Files
* **`PhotoVaultScannerBridge.exe`**: Native 64-bit Windows executable compiled with .NET Framework (27 KB, zero external dependencies, runs on any Windows 10/11 PC).
* **`PhotoVaultScannerBridge.cs`**: Full C# source code. Compiled with Windows' built-in `csc.exe` + `System.Drawing.dll`.
* **`scanner-bridge.ps1`**: Pure PowerShell alternative (runs out-of-the-box without compilation).
* **`run-scanner-bridge.bat`**: Single-click launcher.

---

### API Endpoints
* `GET /status` — Checks bridge health and returns list of connected scanners, supported DPIs (75–1200), and formats (`jpeg`, `tiff`).
* `GET /devices` — Enumerates scanner device IDs and descriptions.
* `POST /scan` — Triggers silent scan with payload:
  ```json
  {
    "dpi": 1200,
    "format": "tiff",
    "colorMode": "color",
    "crop": {
      "x": 0.15,
      "y": 0.20,
      "w": 0.50,
      "h": 0.60
    },
    "deviceId": "{...}"
  }
  ```
  Returns:
  ```json
  {
    "success": true,
    "width": 5100,
    "height": 7014,
    "dpi": 1200,
    "format": "tiff",
    "mime": "image/tiff",
    "isPreview": false,
    "isCropped": true,
    "size": 32182900,
    "preview": "data:image/jpeg;base64,...",
    "data": "data:image/tiff;base64,..."
  }
  ```
