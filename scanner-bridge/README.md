# PhotoVault Scanner Bridge

A lightweight, zero-configuration local bridge that allows PhotoVault (hosted on GitHub Pages or locally) to scan directly from USB flatbed and document scanners.

---

### Why is this needed?
Modern web browsers (Chrome, Edge, Firefox, Safari) have strict hardware sandboxing:
1. **USB Scanners are NOT standard HID devices**: Flatbed and document scanners (HP, Canon, Epson, Brother, Fujitsu) use USB Imaging Class (`0x06`) or vendor bulk transfer endpoints, whereas `navigator.hid` (WebHID) is restricted strictly to Human Interface Devices (keyboards, gamepads, barcode wands).
2. **OS Driver Exclusivity**: Windows automatically claims USB scanners with its native WIA/TWAIN driver stack, which blocks raw browser access via WebUSB.
3. **GitHub Pages Security**: Because PhotoVault runs as a static client-side web app on GitHub Pages (`https://shamsghani.github.io/photovault/`), this tiny local bridge runs on `127.0.0.1:18205` with full CORS and Private Network Access (`Access-Control-Allow-Private-Network`) headers, enabling seamless, zero-latency communication without sending any photos or data to third-party cloud servers.

---

### Quick Start (Windows)
1. Double-click `run-scanner-bridge.bat` (or run `PhotoVaultScannerBridge.exe`).
2. Keep the small console window open in the background while scanning.
3. Open PhotoVault in your browser and click **Scan Photos**.
4. Your scanner will be detected automatically! Select your resolution (150 / 300 / 600 DPI) and click **Start Scan**.

---

### Included Files
* **`PhotoVaultScannerBridge.exe`**: Pre-compiled, ultra-compact native Windows executable (zero external dependencies, runs on any Windows 10/11 PC).
* **`PhotoVaultScannerBridge.cs`**: Full C# source code. Can be compiled at any time with Windows' built-in `csc.exe`.
* **`scanner-bridge.ps1`**: Pure PowerShell alternative (runs out-of-the-box without compilation).
* **`run-scanner-bridge.bat`**: Single-click launcher.

---

### API Endpoints
* `GET /status` — Checks bridge health and returns list of connected scanners.
* `GET /devices` — Enumerates scanner device IDs and descriptions.
* `POST /scan` — Triggers scan with payload `{ "dpi": 300, "colorMode": "color", "showDialog": false }`. Returns `{ "success": true, "data": "data:image/jpeg;base64,...", "width": ..., "height": ... }`.
