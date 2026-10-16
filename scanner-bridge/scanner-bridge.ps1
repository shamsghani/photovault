# PhotoVault Scanner Bridge (PowerShell Edition v1.4)
# Provides local WIA scanner access to PhotoVault on GitHub Pages via localhost HTTP/CORS
# Supports 2-Step Workflow: Fast 75 DPI Preview -> Auto-Crop -> Hardware Cropped 1200 DPI TIFF RAW
$Port = 18205
$Url = "http://127.0.0.1:$Port/"

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  PhotoVault Scanner Bridge v1.4 (PowerShell)   " -ForegroundColor Cyan
Write-Host "  (Fast Preview \u00B7 Auto-Crop \u00B7 1200 DPI TIFF RAW) " -ForegroundColor Cyan
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
Write-Host "  Workflow : 1. Preview (75 DPI) -> 2. Cropped High-Res Scan" -ForegroundColor Green
Write-Host "  Modes    : 75, 100, 200, 300, 600, 1200 DPI | JPEG & TIFF RAW" -ForegroundColor Green
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
                version = "1.4"
                status = "online"
                formats = @("jpeg", "tiff")
                dpis = @(75, 100, 200, 300, 600, 1200)
                supportsHardwareCrop = $true
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
            $cropX = -1; $cropY = -1; $cropW = -1; $cropH = -1
            $hasCrop = $false
            $isPreview = $false

            if ($body) {
                try {
                    $parsed = $body | ConvertFrom-Json
                    if ($parsed.dpi) { $dpi = [int]$parsed.dpi }
                    if ($parsed.colorMode) { $colorMode = [string]$parsed.colorMode }
                    if ($parsed.format) { $format = [string]$parsed.format.ToLower() }
                    if ($parsed.deviceId) { $deviceId = [string]$parsed.deviceId }
                    if ($parsed.isPreview) { $isPreview = [bool]$parsed.isPreview }
                    if ($parsed.crop) {
                        $cropX = [double]$parsed.crop.x
                        $cropY = [double]$parsed.crop.y
                        $cropW = [double]$parsed.crop.w
                        $cropH = [double]$parsed.crop.h
                        if ($cropX -ge 0 -and $cropY -ge 0 -and $cropW -gt 0 -and $cropH -gt 0) { $hasCrop = $true }
                    }
                } catch {}
            }

            if ($dpi -lt 75) { $dpi = 75 }
            if ($dpi -gt 4800) { $dpi = 4800 }

            $isTiff = ($format -in @("tiff", "tif"))
            $targetMime = if ($isTiff) { "image/tiff" } else { "image/jpeg" }
            $targetFmt = if ($isTiff) { "tiff" } else { "jpeg" }

            if ($isPreview) {
                Write-Host "Starting FAST PREVIEW scan ($dpi DPI, full bed)..." -ForegroundColor Cyan
            } elseif ($hasCrop) {
                Write-Host "Starting CROPPED scan ($dpi DPI, region: $cropX,$cropY $cropW x $cropH)..." -ForegroundColor Cyan
            } else {
                Write-Host "Starting scan: DPI=$dpi, Mode=$colorMode, Format=$(if ($isTiff) { 'TIFF RAW' } else { 'JPEG' })" -ForegroundColor Cyan
            }

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

                $maxW = $item.Properties.Item('6151').SubTypeMax
                if (!$maxW) { $maxW = $item.Properties.Item('6151').Value }
                $maxH = $item.Properties.Item('6152').SubTypeMax
                if (!$maxH) { $maxH = $item.Properties.Item('6152').Value }

                $targetW = $maxW; $targetH = $maxH

                if ($hasCrop -and ($cropW -lt 0.995 -or $cropH -lt 0.995 -or $cropX -gt 0.005 -or $cropY -gt 0.005)) {
                    $tx = [int][math]::Max(0, [math]::Min($maxW - 20, [math]::Round($cropX * $maxW)))
                    $ty = [int][math]::Max(0, [math]::Min($maxH - 20, [math]::Round($cropY * $maxH)))
                    $tw = [int][math]::Max(50, [math]::Min($maxW - $tx, [math]::Round($cropW * $maxW)))
                    $th = [int][math]::Max(50, [math]::Min($maxH - $ty, [math]::Round($cropH * $maxH)))
                    $targetW = $tw; $targetH = $th

                    Write-Host "Setting hardware window: X=$tx, Y=$ty, W=$tw, H=$th ($tw x $th px @ $dpi DPI)" -ForegroundColor Magenta
                    try {
                        $item.Properties.Item('6149').Value = 0
                        $item.Properties.Item('6150').Value = 0
                        $item.Properties.Item('6151').Value = $tw
                        $item.Properties.Item('6152').Value = $th
                        $item.Properties.Item('6149').Value = $tx
                        $item.Properties.Item('6150').Value = $ty
                        $item.Properties.Item('6151').Value = $tw
                        $item.Properties.Item('6152').Value = $th
                    } catch {}
                } else {
                    try {
                        $item.Properties.Item('6149').Value = 0
                        $item.Properties.Item('6150').Value = 0
                        $item.Properties.Item('6151').Value = $maxW
                        $item.Properties.Item('6152').Value = $maxH
                    } catch {}
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
                $origBmp = [System.Drawing.Image]::FromStream($msIn)

                $bitmap = $origBmp
                $clonedBmp = $null

                # Software crop fallback if scanner driver returned full bed
                if ($hasCrop -and $origBmp.Width -gt ($targetW * 1.25)) {
                    $subX = [int][math]::Max(0, [math]::Min($origBmp.Width - 10, [math]::Round($cropX * $origBmp.Width)))
                    $subY = [int][math]::Max(0, [math]::Min($origBmp.Height - 10, [math]::Round($cropY * $origBmp.Height)))
                    $subW = [int][math]::Max(20, [math]::Min($origBmp.Width - $subX, [math]::Round($cropW * $origBmp.Width)))
                    $subH = [int][math]::Max(20, [math]::Min($origBmp.Height - $subY, [math]::Round($cropH * $origBmp.Height)))
                    $cropRect = New-Object System.Drawing.Rectangle($subX, $subY, $subW, $subH)
                    $clonedBmp = $origBmp.Clone($cropRect, $origBmp.PixelFormat)
                    $bitmap = $clonedBmp
                }

                $bitmap.SetResolution($dpi, $dpi)
                $w = $bitmap.Width
                $h = $bitmap.Height

                $mainBytes = $null
                $previewBytes = $null

                if ($isTiff) {
                    $msTiff = New-Object System.IO.MemoryStream
                    $bitmap.Save($msTiff, [System.Drawing.Imaging.ImageFormat]::Tiff)
                    $mainBytes = $msTiff.ToArray()
                    $msTiff.Dispose()

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

                if ($clonedBmp) { $clonedBmp.Dispose() }
                $origBmp.Dispose()
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
                    isPreview = $isPreview
                    isCropped = $hasCrop
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
