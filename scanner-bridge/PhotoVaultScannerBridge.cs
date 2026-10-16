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
            Console.Title = "PhotoVault Scanner Bridge (Zero-UI Direct Mode)";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=================================================");
            Console.WriteLine("      PhotoVault Direct Scanner Bridge v1.2      ");
            Console.WriteLine("        (100% Web UI - Zero External Windows)    ");
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
            sb.Append("{\"success\":true,\"version\":\"1.2\",\"status\":\"online\",");
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
            string deviceId = "";

            if (!string.IsNullOrEmpty(body))
            {
                dpi = ParseJsonInt(body, "dpi", 300);
                colorMode = ParseJsonString(body, "colorMode", "color").ToLowerInvariant();
                deviceId = ParseJsonString(body, "deviceId", "");
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Starting silent scan (no external apps): DPI=" + dpi + ", Mode=" + colorMode);
            Console.ResetColor();

            // WIA Intent: 1 = Color, 2 = Grayscale, 4 = Text/BW
            int intent = 1;
            if (colorMode == "grayscale" || colorMode == "gray") intent = 2;
            else if (colorMode == "bw" || colorMode == "text" || colorMode == "lineart") intent = 4;

            string jsonResult = null;
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
                        jsonResult = "{\"success\":false,\"error\":\"No USB scanner connected or available\"}";
                        responseCode = 404;
                        return;
                    }

                    dynamic item = targetDevice.Items[1];

                    // Configure resolution and color intent
                    SetProperty(item.Properties, 6146, intent); // WIA_IPS_CUR_INTENT
                    SetProperty(item.Properties, 6147, dpi);    // WIA_IPS_XRES
                    SetProperty(item.Properties, 6148, dpi);    // WIA_IPS_YRES

                    Console.WriteLine("Triggering direct sensor transfer...");

                    // BMP format GUID is guaranteed to be supported natively by all WIA scanner drivers
                    string bmpGuid = "{B96B3CAB-0728-11D3-9D7B-0000F81EF32E}";
                    dynamic image = null;

                    try
                    {
                        // Direct silent transfer — NO DIALOG, NO EXTERNAL APP
                        image = item.Transfer(bmpGuid);
                    }
                    catch (Exception exBmp)
                    {
                        Console.WriteLine("Direct BMP transfer note: " + exBmp.Message + ". Trying JPEG transfer...");
                        string jpgGuid = "{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}";
                        image = item.Transfer(jpgGuid);
                    }

                    if (image == null)
                    {
                        jsonResult = "{\"success\":false,\"error\":\"Scanner completed without returning image data\"}";
                        return;
                    }

                    byte[] rawBytes = (byte[])image.FileData.BinaryData;
                    int origW = (int)image.Width;
                    int origH = (int)image.Height;

                    // Convert to high-quality compressed JPEG in memory via System.Drawing
                    byte[] jpegBytes;
                    using (var inMs = new MemoryStream(rawBytes))
                    using (var bitmap = Image.FromStream(inMs))
                    using (var outMs = new MemoryStream())
                    {
                        ImageCodecInfo jpgEncoder = GetEncoder(ImageFormat.Jpeg);
                        var encoderParams = new EncoderParameters(1);
                        encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
                        bitmap.Save(outMs, jpgEncoder, encoderParams);
                        jpegBytes = outMs.ToArray();
                    }

                    string base64 = Convert.ToBase64String(jpegBytes);

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Scan successful! " + origW + "x" + origH + " px (" + (jpegBytes.Length / 1024) + " KB JPEG)");
                    Console.WriteLine("Sent directly to PhotoVault web UI.");
                    Console.ResetColor();

                    jsonResult = "{\"success\":true,\"width\":" + origW + ",\"height\":" + origH +
                                  ",\"size\":" + jpegBytes.Length + ",\"mime\":\"image/jpeg\",\"data\":\"data:image/jpeg;base64," + base64 + "\"}";
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Scan error: " + ex.Message);
                    Console.ResetColor();
                    jsonResult = "{\"success\":false,\"error\":" + EscapeJson(ex.Message) + "}";
                    responseCode = 500;
                }
            });

            if (jsonResult == null)
            {
                jsonResult = "{\"success\":false,\"error\":\"Scan operation failed to complete\"}";
                responseCode = 500;
            }

            SendJson(res, jsonResult, responseCode);
        }

        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
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
