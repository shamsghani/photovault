# PhotoVault Scanner Bridge (PowerShell Edition v1.3)
# Provides local WIA scanner access to PhotoVault on GitHub Pages via localhost HTTP/CORS
# Supports 200, 300, 600, 1200 DPI and Lossless TIFF RAW & JPEG output
$Port = 18205
$Url = "http://127.0.0.1:$Port/"

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  PhotoVault Scanner Bridge v1.3 (PowerShell)   " -ForegroundColor Cyan
Write-Host "    (1200 DPI & Lossless TIFF RAW Supported)    " -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host ""

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add($Url)

try {
    $listener.Start()
} catch {
    Write-Host "Error starting listener on $Url : $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Port $Port may already be in use."
    pause
    exit 1
}

Write-Host "  Status   : ACTIVE & LISTENING" -ForegroundColor Green
Write-Host "  Endpoint : $Url" -ForegroundColor Green
Write-Host "  Ready for: https://shamsghani.github.io/photovault/" -ForegroundColor Green
Write-Host "  Modes    : 200, 300, 600, 1200 DPI | JPEG & TIFF RAW" -ForegroundColor Green
Write-Host ""
Write-Host "Keep this window open while scanning photos into PhotoVault."
Write-Host "Press Ctrl+C to terminate."
Write-Host "--------------------------------------------------"

function Send-JsonResponse($response, $jsonStr, $code = 200) {
    $response.Headers.Add("Access-Control-Allow-Origin", "*")
    $response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
    $response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Access-Control-Request-Private-Network, Accept")
    $response.Headers.Add("Access-Control-Allow-Private-Network", "true")
    $response.StatusCode = $code
    $response.ContentType = "application/json; charset=utf-8"
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($jsonStr)
    $response.ContentLength64 = $bytes.Length
    $response.OutputStream.Write($bytes, 0, $bytes.Length)
    $response.OutputStream.Close()
}

function Get-Scanners() {
    $list = @()
    try {
        $dm = New-Object -ComObject WIA.DeviceManager
        foreach ($d in $dm.DeviceInfos) {
            $name = $d.Properties.Item("Name").Value
            $id = $d.DeviceID
            $list += [PSCustomObject]@{ id = $id; name = $name; type = $d.Type }
        }
    } catch {
        Write-Host "WIA enumeration note: $($_.Exception.Message)" -ForegroundColor Yellow
    }
    return $list
}

while ($listener.IsListening) {
    try {
        $context = $listener.GetContext()
        $request = $context.Request
        $response = $context.Response
        $method = $request.HttpMethod.ToUpper()
        $path = $request.Url.AbsolutePath.ToLower()

        if ($method -eq "OPTIONS") {
            $response.Headers.Add("Access-Control-Allow-Origin", "*")
            $response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
            $response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Access-Control-Request-Private-Network, Accept")
            $response.Headers.Add("Access-Control-Allow-Private-Network", "true")
            $response.StatusCode = 204
            $response.Close()
            continue
        }

        Write-Host "[$([DateTime]::Now.ToString('HH:mm:ss'))] $method $path"

        if ($path -in @("/", "/status", "/ping")) {
            $devices = Get-Scanners
            $json = @{
                success = $true
                version = "1.3"
                status = "online"
                formats = @("jpeg", "tiff")
                dpis = @(200, 300, 600, 1200)
                devices = $devices
            } | ConvertTo-Json -Compress
            Send-JsonResponse $response $json
        }
        elseif ($path -eq "/devices") {
            $devices = Get-Scanners
            $json = @{
                success = $true
                devices = $devices
            } | ConvertTo-Json -Compress
            Send-JsonResponse $response $json
        }
        elseif ($path -eq "/scan" -and $method -eq "POST") {
            $reader = New-Object System.IO.StreamReader($request.InputStream, $request.ContentEncoding)
            $body = $reader.ReadToEnd()
            $reader.Close()

            $dpi = 300
            $colorMode = "color"
            $format = "jpeg"
            $deviceId = ""

            if ($body) {
                try {
                    $parsed = $body | ConvertFrom-Json
                    if ($parsed.dpi) { $dpi = [int]$parsed.dpi }
                    if ($parsed.colorMode) { $colorMode = [string]$parsed.colorMode }
                    if ($parsed.format) { $format = [string]$parsed.format.ToLower() }
                    if ($parsed.deviceId) { $deviceId = [string]$parsed.deviceId }
                } catch {}
            }

            if ($dpi -lt 75) { $dpi = 75 }
            if ($dpi -gt 4800) { $dpi = 4800 }

            $isTiff = ($format -in @("tiff", "tif"))
            $targetMime = if ($isTiff) { "image/tiff" } else { "image/jpeg" }
            $targetFmt = if ($isTiff) { "tiff" } else { "jpeg" }

            Write-Host "Starting silent scan: DPI=$dpi, Mode=$colorMode, Format=$(if ($isTiff) { 'TIFF RAW' } else { 'JPEG' })" -ForegroundColor Cyan

            $intent = 1 # Color
            if ($colorMode -in @("grayscale", "gray")) { $intent = 2 }
            elseif ($colorMode -in @("bw", "text", "lineart")) { $intent = 4 }

            $tiffGuid = "{B96B3CB1-0728-11D3-9D7B-0000F81EF32E}"
            $bmpGuid  = "{B96B3CAB-0728-11D3-9D7B-0000F81EF32E}"
            $jpegGuid = "{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}"

            try {
                $dm = New-Object -ComObject WIA.DeviceManager
                $dev = $null
                foreach ($d in $dm.DeviceInfos) {
                    if (!$deviceId -or $d.DeviceID -eq $deviceId -or $d.DeviceID.Contains($deviceId)) {
                        Write-Host "Connecting to $($d.Properties.Item('Name').Value)..." -ForegroundColor Cyan
                        $dev = $d.Connect()
                        break
                    }
                }
                if (!$dev -and $dm.DeviceInfos.Count -gt 0) {
                    $dev = $dm.DeviceInfos.Item(1).Connect()
                }

                if (!$dev) {
                    Send-JsonResponse $response (@{ success = $false; error = "No scanner connected" } | ConvertTo-Json) 404
                    continue
                }

                $item = $dev.Items.Item(1)
                # Safely set intent and resolution directly on optical sensor
                foreach ($p in $item.Properties) {
                    if ($p.PropertyID -eq 6146) { try { $p.Value = $intent } catch {} }
                    if ($p.PropertyID -eq 6147) { try { $p.Value = $dpi } catch {} }
                    if ($p.PropertyID -eq 6148) { try { $p.Value = $dpi } catch {} }
                }

                Write-Host "Scanning from sensor directly ($dpi DPI)..." -ForegroundColor Cyan
                $image = $null
                if ($isTiff) {
                    try {
                        $image = $item.Transfer($tiffGuid)
                    } catch {
                        try {
                            $image = $item.Transfer($bmpGuid)
                        } catch {
                            $image = $item.Transfer($jpegGuid)
                        }
                    }
                } else {
                    try {
                        $image = $item.Transfer($bmpGuid)
                    } catch {
                        $image = $item.Transfer($jpegGuid)
                    }
                }

                if ($image -eq $null) {
                    Send-JsonResponse $response (@{ success = $false; error = "Scan produced no image" } | ConvertTo-Json) 500
                    continue
                }

                $rawBytes = $image.FileData.BinaryData
                [System.Reflection.Assembly]::LoadWithPartialName("System.Drawing") | Out-Null
                $msIn = New-Object System.IO.MemoryStream(,$rawBytes)
                $bitmap = [System.Drawing.Image]::FromStream($msIn)
                $bitmap.SetResolution($dpi, $dpi)

                $w = $bitmap.Width
                $h = $bitmap.Height

                $mainBytes = $null
                $previewBytes = $null

                if ($isTiff) {
                    # Encode lossless TIFF
                    $msTiff = New-Object System.IO.MemoryStream
                    $bitmap.Save($msTiff, [System.Drawing.Imaging.ImageFormat]::Tiff)
                    $mainBytes = $msTiff.ToArray()
                    $msTiff.Dispose()

                    # Companion preview JPEG for instantaneous browser canvas
                    $msPrev = New-Object System.IO.MemoryStream
                    $bitmap.Save($msPrev, [System.Drawing.Imaging.ImageFormat]::Jpeg)
                    $previewBytes = $msPrev.ToArray()
                    $msPrev.Dispose()
                } else {
                    $msOut = New-Object System.IO.MemoryStream
                    $bitmap.Save($msOut, [System.Drawing.Imaging.ImageFormat]::Jpeg)
                    $mainBytes = $msOut.ToArray()
                    $previewBytes = $mainBytes
                    $msOut.Dispose()
                }

                $bitmap.Dispose()
                $msIn.Dispose()

                $mainBase64 = [Convert]::ToBase64String($mainBytes)
                $prevBase64 = if ($previewBytes) { [Convert]::ToBase64String($previewBytes) } else { $null }

                Write-Host "Scan completed! Dimensions: ${w}x${h} @ ${dpi} DPI, $([math]::Round($mainBytes.Length/1024)) KB $(if ($isTiff) { 'TIFF RAW' } else { 'JPEG' })" -ForegroundColor Green

                $respObj = @{
                    success = $true
                    width = $w
                    height = $h
                    dpi = $dpi
                    format = $targetFmt
                    mime = $targetMime
                    size = $mainBytes.Length
                    preview = if ($prevBase64) { "data:image/jpeg;base64,$prevBase64" } else { $null }
                    data = "data:${targetMime};base64,$mainBase64"
                }
                Send-JsonResponse $response ($respObj | ConvertTo-Json -Compress)
            } catch {
                Write-Host "Scan error: $($_.Exception.Message)" -ForegroundColor Red
                Send-JsonResponse $response (@{ success = $false; error = $_.Exception.Message } | ConvertTo-Json) 500
            }
        }
        else {
            Send-JsonResponse $response (@{ error = "Not found" } | ConvertTo-Json) 404
        }
    } catch {
        Write-Host "Listener error: $($_.Exception.Message)" -ForegroundColor Red
    }
}
