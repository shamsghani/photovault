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

            try {
                $cd = New-Object -ComObject WIA.CommonDialog
                $image = $null
                
                # Use acquire image dialog for interactive scan preview & crop
                $image = $cd.ShowAcquireImage(1, $intent, 131072, $jpegGuid, $false, $true, $false)

                if ($image -eq $null) {
                    Send-JsonResponse $response (@{ success = $false; error = "Scan was cancelled" } | ConvertTo-Json)
                    continue
                }

                $bytes = $image.FileData.BinaryData
                $base64 = [Convert]::ToBase64String($bytes)
                $w = $image.Width
                $h = $image.Height

                Write-Host "Scan completed: ${w}x${h}, $([math]::Round($bytes.Length/1024)) KB" -ForegroundColor Green

                $respObj = @{
                    success = $true
                    width = $w
                    height = $h
                    size = $bytes.Length
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
