# PhotoVault Scanner Bridge (PowerShell Edition)
# Provides local WIA scanner access to PhotoVault on GitHub Pages via localhost HTTP/CORS
$Port = 18205
$Url = "http://127.0.0.1:$Port/"

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "    PhotoVault Scanner Bridge (PowerShell)      " -ForegroundColor Cyan
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
Write-Host ""
Write-Host "Keep this window open while scanning photos into PhotoVault."
Write-Host "Press Ctrl+C to terminate."
Write-Host "--------------------------------------------------"

function Send-JsonResponse($response, $jsonStr, $code = 200) {
    $response.Headers.Add("Access-Control-Allow-Origin", "*")
    $response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
    $response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Access-Control-Request-Private-Network")
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
            $response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Access-Control-Request-Private-Network")
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
                version = "1.0"
                status = "online"
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
            $showDialog = $false
            $deviceId = ""

            if ($body) {
                try {
                    $parsed = $body | ConvertFrom-Json
                    if ($parsed.dpi) { $dpi = [int]$parsed.dpi }
                    if ($parsed.colorMode) { $colorMode = [string]$parsed.colorMode }
                    if ($parsed.showDialog -ne $null) { $showDialog = [bool]$parsed.showDialog }
                    if ($parsed.deviceId) { $deviceId = [string]$parsed.deviceId }
                } catch {}
            }

            $intent = 1 # Color
            if ($colorMode -in @("grayscale", "gray")) { $intent = 2 }
            elseif ($colorMode -in @("bw", "text")) { $intent = 4 }

            $jpegGuid = "{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}"

            $bmpGuid = "{B96B3CAB-0728-11D3-9D7B-0000F81EF32E}"
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
                # Safely set intent and resolution
                foreach ($p in $item.Properties) {
                    if ($p.PropertyID -eq 6146) { try { $p.Value = $intent } catch {} }
                    if ($p.PropertyID -eq 6147) { try { $p.Value = $dpi } catch {} }
                    if ($p.PropertyID -eq 6148) { try { $p.Value = $dpi } catch {} }
                }

                Write-Host "Scanning from sensor directly (no external window)..." -ForegroundColor Cyan
                $image = $null
                try {
                    $image = $item.Transfer($bmpGuid)
                } catch {
                    $image = $item.Transfer($jpegGuid)
                }

                if ($image -eq $null) {
                    Send-JsonResponse $response (@{ success = $false; error = "Scan produced no image" } | ConvertTo-Json) 500
                    continue
                }

                $rawBytes = $image.FileData.BinaryData
                [System.Reflection.Assembly]::LoadWithPartialName("System.Drawing") | Out-Null
                $msIn = New-Object System.IO.MemoryStream(,$rawBytes)
                $bitmap = [System.Drawing.Image]::FromStream($msIn)
                $msOut = New-Object System.IO.MemoryStream
                $bitmap.Save($msOut, [System.Drawing.Imaging.ImageFormat]::Jpeg)
                $jpegBytes = $msOut.ToArray()
                $base64 = [Convert]::ToBase64String($jpegBytes)
                $w = $image.Width
                $h = $image.Height
                $bitmap.Dispose()
                $msIn.Dispose()
                $msOut.Dispose()

                Write-Host "Scan completed! Dimensions: ${w}x${h}, $([math]::Round($jpegBytes.Length/1024)) KB JPEG" -ForegroundColor Green

                $respObj = @{
                    success = $true
                    width = $w
                    height = $h
                    size = $jpegBytes.Length
                    mime = "image/jpeg"
                    data = "data:image/jpeg;base64,$base64"
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
