# PhotoVault Scanner Bridge (v1.2)

A lightweight, zero-configuration local bridge that allows PhotoVault (hosted on GitHub Pages or locally) to scan directly from USB flatbed and document scanners **100% inside your browser with zero external apps or popups**.

---

### Why is this needed?
Modern web browsers (Chrome, Edge, Firefox, Safari) have strict hardware sandboxing:
1. **USB Scanners are NOT standard HID devices**: Flatbed and document scanners (HP, Canon, Epson, Brother, Fujitsu) use USB Imaging Class (`0x06`) or vendor bulk transfer endpoints, whereas `navigator.hid` (WebHID) is restricted strictly to Human Interface Devices (keyboards, gamepads, barcode wands).
2. **OS Driver Exclusivity**: Windows automatically claims USB scanners with its native WIA/TWAIN driver stack, which blocks raw browser access via WebUSB.
3. **GitHub Pages Security**: Because PhotoVault runs as a static client-side web app on GitHub Pages (`https://shamsghani.github.io/photovault/`), this tiny local bridge runs on `127.0.0.1:18205` with full CORS and Private Network Access (`Access-Control-Allow-Private-Network`) headers, enabling seamless, zero-latency communication without sending any photos or data to third-party cloud servers.
4. **100% In-App UI**: The bridge acquires scans directly from the optical sensor via direct memory transfer and returns clean compressed JPEG streams to the browser. **No external Windows Scan apps or HP software windows open**. All previewing, cropping, orientation, color correction, and tagging happen right inside the PhotoVault web application.

---

### Quick Start (Windows)
1. Double-click `run-scanner-bridge.bat` (or run `PhotoVaultScannerBridge.exe`).
2. Keep the small console window open in the background while scanning.
3. Open PhotoVault in your browser and click **Scan Photos**.
4. Your scanner is detected automatically. Select your resolution (200 / 300 / 600 DPI) and click **Scan Photo Directly**.
5. The scanned photo opens immediately in PhotoVault's **In-App Scan Studio**:
   - **Auto-Crop Photo**: Automatically detects photo edges on the glass bed.
   - **Interactive Crop Handles**: Freeform or standard photo ratios (4×6, 5×7, 1:1, Full Bed).
   - **Rotation & Flip**: 90° CCW / CW, Horizontal and Vertical flip.
   - **Visual Adjustments**: Contrast and Brightness sliders + Auto-Enhance for vintage prints.
   - **Save to Vault**: Saves cropped photo directly to your computer folder and library.

---

### Included Files
* **`PhotoVaultScannerBridge.exe`**: Pre-compiled, ultra-compact native Windows executable (27 KB, zero external dependencies, runs on any Windows 10/11 PC).
* **`PhotoVaultScannerBridge.cs`**: Full C# source code. Compiled with Windows' built-in `csc.exe` + `System.Drawing.dll`.
* **`scanner-bridge.ps1`**: Pure PowerShell alternative (runs out-of-the-box without compilation).
* **`run-scanner-bridge.bat`**: Single-click launcher.

---

### API Endpoints
* `GET /status` — Checks bridge health and returns list of connected scanners.
* `GET /devices` — Enumerates scanner device IDs and descriptions.
* `POST /scan` — Triggers silent scan with payload `{ "dpi": 300, "colorMode": "color", "deviceId": "..." }`. Returns `{ "success": true, "data": "data:image/jpeg;base64,...", "width": ..., "height": ... }`.

