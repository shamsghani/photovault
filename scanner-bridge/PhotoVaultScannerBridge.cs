using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

namespace PhotoVault.ScannerBridge
{
    class Program
    {
        private const int PORT = 18205;
        private static HttpListener listener;
        private static bool isRunning = true;

        static void Main(string[] args)
        {
            Console.Title = "PhotoVault Scanner Bridge (1200 DPI & TIFF RAW Supported)";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=================================================");
            Console.WriteLine("      PhotoVault Direct Scanner Bridge v1.3      ");
            Console.WriteLine("   (100% In-Browser UI \u00B7 1200 DPI \u00B7 TIFF RAW)    ");
            Console.WriteLine("=================================================");
            Console.ResetColor();
            Console.WriteLine();

            TestWiaAvailability();

            string url = "http://127.0.0.1:" + PORT + "/";
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add(url);
                listener.Start();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("ERROR: Could not bind to " + url + ": " + ex.Message);
                Console.WriteLine("Make sure port " + PORT + " is not already in use.");
                Console.ResetColor();
                Console.WriteLine("\nPress any key to exit...");
                Console.ReadKey();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  Status   : ACTIVE & READY FOR PHOTOVAULT");
            Console.WriteLine("  Endpoint : " + url);
            Console.WriteLine("  Web UI   : https://shamsghani.github.io/photovault/");
            Console.WriteLine("  Modes    : 200, 300, 600, 1200 DPI | JPEG & TIFF RAW");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("Scans are controlled 100% inside your browser.");
            Console.WriteLine("No external Windows or HP apps will open.");
            Console.WriteLine("Keep this background helper open while using PhotoVault.");
            Console.WriteLine(new string('-', 50));

            while (isRunning)
            {
                try
                {
                    HttpListenerContext ctx = listener.GetContext();
                    ThreadPool.QueueUserWorkItem((state) => HandleRequest((HttpListenerContext)state), ctx);
                }
                catch (Exception)
                {
                    if (!isRunning) break;
                }
            }
        }

        private static void TestWiaAvailability()
        {
            ExecuteInSta(() =>
            {
                try
                {
                    Type dmType = Type.GetTypeFromProgID("WIA.DeviceManager");
                    if (dmType == null)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("WARNING: WIA COM library (WIA.DeviceManager) not found.");
                        Console.ResetColor();
                        return;
                    }
                    dynamic dm = Activator.CreateInstance(dmType);
                    int count = dm.DeviceInfos.Count;
                    Console.WriteLine("Scanner Subsystem: Ready (" + count + " device" + (count == 1 ? "" : "s") + " detected)");
                    foreach (dynamic info in dm.DeviceInfos)
                    {
                        try
                        {
                            string name = (string)info.Properties["Name"].Value;
                            string id = (string)info.DeviceID;
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("  * " + name + " [" + id + "]");
                            Console.ResetColor();
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("WIA check notice: " + ex.Message);
                    Console.ResetColor();
                }
            });
        }

        private static void ExecuteInSta(Action action)
        {
            Exception threadEx = null;
            Thread staThread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    threadEx = ex;
                }
            });
            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();
            staThread.Join();
            if (threadEx != null) throw threadEx;
        }

        private static void AddCorsHeaders(HttpListenerResponse res)
        {
            res.Headers["Access-Control-Allow-Origin"] = "*";
            res.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
            res.Headers["Access-Control-Allow-Headers"] = "Content-Type, Access-Control-Request-Private-Network, Accept";
            res.Headers["Access-Control-Allow-Private-Network"] = "true";
        }

        private static void SendJson(HttpListenerResponse res, string json, int statusCode = 200)
        {
            try
            {
                AddCorsHeaders(res);
                res.StatusCode = statusCode;
                res.ContentType = "application/json; charset=utf-8";
                byte[] buf = Encoding.UTF8.GetBytes(json);
                res.ContentLength64 = buf.Length;
                res.OutputStream.Write(buf, 0, buf.Length);
                res.OutputStream.Close();
            }
            catch { }
        }

        private static void HandleRequest(HttpListenerContext ctx)
        {
            HttpListenerRequest req = ctx.Request;
            HttpListenerResponse res = ctx.Response;

            string path = req.Url.AbsolutePath.ToLowerInvariant();
            string method = req.HttpMethod.ToUpperInvariant();

            // Handle preflight CORS request
            if (method == "OPTIONS")
            {
                AddCorsHeaders(res);
                res.StatusCode = 204;
                res.Close();
                return;
            }

            Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + method + " " + path);

            try
            {
                if (path == "/" || path == "/status" || path == "/ping")
                {
                    HandleStatus(res);
                }
                else if (path == "/devices")
                {
                    HandleDevices(res);
                }
                else if (path == "/scan" && method == "POST")
                {
                    HandleScan(req, res);
                }
                else
                {
                    SendJson(res, "{\"error\":\"Not found\"}", 404);
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Request error: " + ex.Message);
                Console.ResetColor();
                SendJson(res, "{\"success\":false,\"error\":" + EscapeJson(ex.Message) + "}", 500);
            }
        }

        private static void HandleStatus(HttpListenerResponse res)
        {
            List<string> deviceList = GetScannerList();
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"success\":true,\"version\":\"1.3\",\"status\":\"online\",");
            sb.Append("\"formats\":[\"jpeg\",\"tiff\"],");
            sb.Append("\"dpis\":[200,300,600,1200],");
            sb.Append("\"devices\":[");
            for (int i = 0; i < deviceList.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(deviceList[i]);
            }
            sb.Append("]}");
            SendJson(res, sb.ToString());
        }

        private static void HandleDevices(HttpListenerResponse res)
        {
            List<string> deviceList = GetScannerList();
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"success\":true,\"devices\":[");
            for (int i = 0; i < deviceList.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(deviceList[i]);
            }
            sb.Append("]}");
            SendJson(res, sb.ToString());
        }

        private static List<string> GetScannerList()
        {
            List<string> list = new List<string>();
            ExecuteInSta(() =>
            {
                try
                {
                    Type dmType = Type.GetTypeFromProgID("WIA.DeviceManager");
                    if (dmType != null)
                    {
                        dynamic dm = Activator.CreateInstance(dmType);
                        foreach (dynamic info in dm.DeviceInfos)
                        {
                            try
                            {
                                string name = (string)info.Properties["Name"].Value;
                                string id = (string)info.DeviceID;
                                int type = (int)info.Type;
                                list.Add("{\"id\":" + EscapeJson(id) + ",\"name\":" + EscapeJson(name) + ",\"type\":" + type + "}");
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error enumerating devices: " + ex.Message);
                }
            });
            return list;
        }

        private static void HandleScan(HttpListenerRequest req, HttpListenerResponse res)
        {
            string body = "";
            using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
            {
                body = reader.ReadToEnd();
            }

            int dpi = 300;
            string colorMode = "color";
            string format = "jpeg";
            string deviceId = "";

            if (!string.IsNullOrEmpty(body))
            {
                dpi = ParseJsonInt(body, "dpi", 300);
                colorMode = ParseJsonString(body, "colorMode", "color").ToLowerInvariant();
                format = ParseJsonString(body, "format", "jpeg").ToLowerInvariant();
                deviceId = ParseJsonString(body, "deviceId", "");
            }

            if (dpi < 75) dpi = 75;
            if (dpi > 4800) dpi = 4800;

            bool isTiff = (format == "tiff" || format == "tif");
            string targetMime = isTiff ? "image/tiff" : "image/jpeg";
            string targetFmt = isTiff ? "tiff" : "jpeg";

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Starting silent scan (no external apps): DPI=" + dpi + ", Mode=" + colorMode + ", Format=" + (isTiff ? "TIFF RAW (Lossless)" : "JPEG"));
            Console.ResetColor();

            // WIA Intent: 1 = Color, 2 = Grayscale, 4 = Text/BW
            int intent = 1;
            if (colorMode == "grayscale" || colorMode == "gray") intent = 2;
            else if (colorMode == "bw" || colorMode == "text" || colorMode == "lineart") intent = 4;

            bool scanSucceeded = false;
            string errorMessage = null;
            int responseCode = 200;

            ExecuteInSta(() =>
            {
                try
                {
                    Type dmType = Type.GetTypeFromProgID("WIA.DeviceManager");
                    dynamic dm = Activator.CreateInstance(dmType);
                    dynamic targetDevice = null;

                    foreach (dynamic d in dm.DeviceInfos)
                    {
                        string curId = (string)d.DeviceID;
                        if (string.IsNullOrEmpty(deviceId) || curId.Equals(deviceId, StringComparison.OrdinalIgnoreCase) || curId.Contains(deviceId) || deviceId.Contains(curId))
                        {
                            Console.WriteLine("Connected to: " + d.Properties["Name"].Value);
                            targetDevice = d.Connect();
                            break;
                        }
                    }

                    if (targetDevice == null && dm.DeviceInfos.Count > 0)
                    {
                        Console.WriteLine("Using default scanner: " + dm.DeviceInfos[1].Properties["Name"].Value);
                        targetDevice = dm.DeviceInfos[1].Connect();
                    }

                    if (targetDevice == null)
                    {
                        errorMessage = "No USB scanner connected or available";
                        responseCode = 404;
                        return;
                    }

                    dynamic item = targetDevice.Items[1];

                    // Configure resolution and color intent directly on hardware sensor
                    SetProperty(item.Properties, 6146, intent); // WIA_IPS_CUR_INTENT
                    SetProperty(item.Properties, 6147, dpi);    // WIA_IPS_XRES
                    SetProperty(item.Properties, 6148, dpi);    // WIA_IPS_YRES

                    Console.WriteLine("Triggering direct optical sensor transfer (" + dpi + " DPI)...");

                    // Standard WIA format GUIDs
                    string tiffGuid = "{B96B3CB1-0728-11D3-9D7B-0000F81EF32E}";
                    string bmpGuid  = "{B96B3CAB-0728-11D3-9D7B-0000F81EF32E}";
                    string jpgGuid  = "{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}";
                    dynamic image = null;

                    if (isTiff)
                    {
                        try
                        {
                            // Attempt native direct hardware TIFF transfer
                            image = item.Transfer(tiffGuid);
                        }
                        catch (Exception exTiff)
                        {
                            Console.WriteLine("Native TIFF transfer notice: " + exTiff.Message + ". Transferring uncompressed BMP from sensor...");
                            try
                            {
                                image = item.Transfer(bmpGuid);
                            }
                            catch (Exception exBmp)
                            {
                                Console.WriteLine("BMP transfer notice: " + exBmp.Message + ". Transferring JPEG...");
                                image = item.Transfer(jpgGuid);
                            }
                        }
                    }
                    else
                    {
                        try
                        {
                            // Direct silent transfer BMP
                            image = item.Transfer(bmpGuid);
                        }
                        catch (Exception exBmp)
                        {
                            Console.WriteLine("BMP transfer note: " + exBmp.Message + ". Transferring JPEG...");
                            image = item.Transfer(jpgGuid);
                        }
                    }

                    if (image == null)
                    {
                        errorMessage = "Scanner completed without returning image data";
                        responseCode = 500;
                        return;
                    }

                    byte[] rawBytes = (byte[])image.FileData.BinaryData;
                    int origW = (int)image.Width;
                    int origH = (int)image.Height;

                    byte[] mainBytes = null;
                    byte[] previewBytes = null;

                    using (var inMs = new MemoryStream(rawBytes))
                    using (var bitmap = new Bitmap(inMs))
                    {
                        bitmap.SetResolution(dpi, dpi);
                        origW = bitmap.Width;
                        origH = bitmap.Height;

                        if (isTiff)
                        {
                            // 1. Lossless RAW TIFF encoding with LZW compression
                            using (var tiffMs = new MemoryStream())
                            {
                                ImageCodecInfo tiffCodec = GetEncoder(ImageFormat.Tiff);
                                if (tiffCodec != null)
                                {
                                    using (var encParams = new EncoderParameters(1))
                                    {
                                        encParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Compression, (long)EncoderValue.CompressionLZW);
                                        bitmap.Save(tiffMs, tiffCodec, encParams);
                                    }
                                }
                                else
                                {
                                    bitmap.Save(tiffMs, ImageFormat.Tiff);
                                }
                                mainBytes = tiffMs.ToArray();
                            }

                            // 2. Companion high-quality JPEG preview for instantaneous in-browser canvas rendering
                            using (var prevMs = new MemoryStream())
                            {
                                ImageCodecInfo jpgEncoder = GetEncoder(ImageFormat.Jpeg);
                                using (var encParams = new EncoderParameters(1))
                                {
                                    encParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);
                                    bitmap.Save(prevMs, jpgEncoder, encParams);
                                }
                                previewBytes = prevMs.ToArray();
                            }
                        }
                        else
                        {
                            // High-quality JPEG
                            using (var outMs = new MemoryStream())
                            {
                                ImageCodecInfo jpgEncoder = GetEncoder(ImageFormat.Jpeg);
                                using (var encoderParams = new EncoderParameters(1))
                                {
                                    encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
                                    bitmap.Save(outMs, jpgEncoder, encoderParams);
                                }
                                mainBytes = outMs.ToArray();
                            }
                            previewBytes = mainBytes;
                        }
                    }

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Scan successful! " + origW + "x" + origH + " px @ " + dpi + " DPI (" + (mainBytes.Length / 1024) + " KB " + (isTiff ? "TIFF RAW" : "JPEG") + ")");
                    Console.WriteLine("Sent directly to PhotoVault web UI (Zero external apps).");
                    Console.ResetColor();

                    SendScanResponse(res, origW, origH, dpi, targetFmt, targetMime, mainBytes, previewBytes);
                    scanSucceeded = true;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Scan error: " + ex.Message);
                    Console.ResetColor();
                    errorMessage = ex.Message;
                    responseCode = 500;
                }
            });

            if (!scanSucceeded)
            {
                SendJson(res, "{\"success\":false,\"error\":" + EscapeJson(errorMessage ?? "Scan operation failed to complete") + "}", responseCode);
            }
        }

        private static void SendScanResponse(HttpListenerResponse res, int origW, int origH, int dpi, string format, string mime, byte[] mainBytes, byte[] previewBytes)
        {
            try
            {
                AddCorsHeaders(res);
                res.StatusCode = 200;
                res.ContentType = "application/json; charset=utf-8";

                using (var sw = new StreamWriter(res.OutputStream, Encoding.UTF8, 65536))
                {
                    sw.Write("{\"success\":true,\"width\":");
                    sw.Write(origW);
                    sw.Write(",\"height\":");
                    sw.Write(origH);
                    sw.Write(",\"dpi\":");
                    sw.Write(dpi);
                    sw.Write(",\"format\":");
                    sw.Write(EscapeJson(format));
                    sw.Write(",\"mime\":");
                    sw.Write(EscapeJson(mime));
                    sw.Write(",\"size\":");
                    sw.Write(mainBytes.Length);

                    if (previewBytes != null && previewBytes.Length > 0)
                    {
                        sw.Write(",\"preview\":\"data:image/jpeg;base64,");
                        WriteBase64ToStream(sw, previewBytes);
                        sw.Write("\"");
                    }
                    else
                    {
                        sw.Write(",\"preview\":null");
                    }

                    sw.Write(",\"data\":\"data:");
                    sw.Write(mime);
                    sw.Write(";base64,");
                    WriteBase64ToStream(sw, mainBytes);
                    sw.Write("\"}");
                    sw.Flush();
                }
                res.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error sending scan response: " + ex.Message);
            }
        }

        private static void WriteBase64ToStream(StreamWriter sw, byte[] data)
        {
            char[] base64Chars = new char[8192];
            int offset = 0;
            while (offset < data.Length)
            {
                int chunkLen = Math.Min(5760, data.Length - offset);
                int charsWritten = Convert.ToBase64CharArray(data, offset, chunkLen, base64Chars, 0);
                sw.Write(base64Chars, 0, charsWritten);
                offset += chunkLen;
            }
        }

        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid) return codec;
            }
            return null;
        }

        private static void SetProperty(dynamic props, int propId, object val)
        {
            try
            {
                foreach (dynamic p in props)
                {
                    if ((int)p.PropertyID == propId)
                    {
                        p.Value = val;
                        return;
                    }
                }
            }
            catch { }
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return "\"\"";
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\b') sb.Append("\\b");
                else if (c == '\f') sb.Append("\\f");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < 32) sb.AppendFormat("\\u{0:x4}", (int)c);
                else sb.Append(c);
            }
            sb.Append("\"");
            return sb.ToString();
        }

        private static string ParseJsonString(string json, string key, string def)
        {
            try
            {
                string needle = "\"" + key + "\"";
                int idx = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return def;
                int colon = json.IndexOf(':', idx + needle.Length);
                if (colon < 0) return def;
                int startQuote = json.IndexOf('\"', colon + 1);
                if (startQuote < 0) return def;
                int endQuote = json.IndexOf('\"', startQuote + 1);
                while (endQuote > startQuote && json[endQuote - 1] == '\\')
                {
                    endQuote = json.IndexOf('\"', endQuote + 1);
                }
                if (endQuote < 0) return def;
                string val = json.Substring(startQuote + 1, endQuote - startQuote - 1);
                return val.Replace("\\\\", "\\").Replace("\\\"", "\"");
            }
            catch { return def; }
        }

        private static int ParseJsonInt(string json, string key, int def)
        {
            try
            {
                string needle = "\"" + key + "\"";
                int idx = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return def;
                int colon = json.IndexOf(':', idx + needle.Length);
                if (colon < 0) return def;
                int end = json.IndexOfAny(new char[] { ',', '}', '\r', '\n' }, colon + 1);
                string valStr = (end > 0 ? json.Substring(colon + 1, end - colon - 1) : json.Substring(colon + 1)).Trim();
                int result;
                if (int.TryParse(valStr, out result)) return result;
                return def;
            }
            catch { return def; }
        }
    }
}
