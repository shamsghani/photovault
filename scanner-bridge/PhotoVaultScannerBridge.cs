using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Collections.Generic;

namespace PhotoVault.ScannerBridge
{
    class Program
    {
        private const int PORT = 18205;
        private static HttpListener listener;
        private static bool isRunning = true;

        static void Main(string[] args)
        {
            Console.Title = "PhotoVault Scanner Bridge";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=================================================");
            Console.WriteLine("     PhotoVault Scanner Bridge (WIA to Web)      ");
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
            Console.WriteLine("  Status   : ACTIVE & LISTENING");
            Console.WriteLine("  Endpoint : " + url);
            Console.WriteLine("  Web URL  : Ready for https://shamsghani.github.io/photovault/");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("Keep this window open while scanning photos into PhotoVault.");
            Console.WriteLine("Press Ctrl+C to close at any time.");
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
            try
            {
                Type dmType = Type.GetTypeFromProgID("WIA.DeviceManager");
                if (dmType == null)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("WARNING: WIA COM library (WIA.DeviceManager) not found on this system.");
                    Console.ResetColor();
                    return;
                }
                dynamic dm = Activator.CreateInstance(dmType);
                int count = dm.DeviceInfos.Count;
                Console.WriteLine("WIA Imaging Subsystem: Ready (" + count + " scanner/imaging device" + (count == 1 ? "" : "s") + " detected)");
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
        }

        private static void AddCorsHeaders(HttpListenerResponse res)
        {
            res.Headers["Access-Control-Allow-Origin"] = "*";
            res.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
            res.Headers["Access-Control-Allow-Headers"] = "Content-Type, Access-Control-Request-Private-Network";
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
            sb.Append("{\"success\":true,\"version\":\"1.0\",\"status\":\"online\",");
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
            bool showDialog = false;
            string deviceId = "";

            if (!string.IsNullOrEmpty(body))
            {
                dpi = ParseJsonInt(body, "dpi", 300);
                colorMode = ParseJsonString(body, "colorMode", "color").ToLowerInvariant();
                showDialog = ParseJsonBool(body, "showDialog", false);
                deviceId = ParseJsonString(body, "deviceId", "");
            }

            Console.WriteLine("Initiating scan: DPI=" + dpi + ", Mode=" + colorMode + ", Dialog=" + showDialog);

            // WIA Intent: 1 = Color, 2 = Grayscale, 4 = Text/BW
            int intent = 1;
            if (colorMode == "grayscale" || colorMode == "gray") intent = 2;
            else if (colorMode == "bw" || colorMode == "text" || colorMode == "lineart") intent = 4;

            // Format: WIA JPEG GUID
            string formatGuid = "{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}"; // JPEG

            try
            {
                dynamic image = null;

                if (showDialog || string.IsNullOrEmpty(deviceId))
                {
                    // Use WIA CommonDialog to acquire image
                    Type cdType = Type.GetTypeFromProgID("WIA.CommonDialog");
                    dynamic cd = Activator.CreateInstance(cdType);
                    // DeviceType: 1 = ScannerDeviceType
                    // Bias: 131072 = Maximize Quality
                    image = cd.ShowAcquireImage(1, intent, 131072, formatGuid, false, true, false);
                }
                else
                {
                    // Direct acquire from specific device
                    Type dmType = Type.GetTypeFromProgID("WIA.DeviceManager");
                    dynamic dm = Activator.CreateInstance(dmType);
                    dynamic targetDevice = null;
                    foreach (dynamic d in dm.DeviceInfos)
                    {
                        if ((string)d.DeviceID == deviceId)
                        {
                            targetDevice = d.Connect();
                            break;
                        }
                    }

                    if (targetDevice == null)
                    {
                        // Fall back to dialog
                        Type cdType = Type.GetTypeFromProgID("WIA.CommonDialog");
                        dynamic cd = Activator.CreateInstance(cdType);
                        image = cd.ShowAcquireImage(1, intent, 131072, formatGuid, false, true, false);
                    }
                    else
                    {
                        dynamic item = targetDevice.Items[1];
                        // Set DPI and intent properties if supported
                        SetProperty(item.Properties, "6146", intent); // WIA_IPS_CUR_INTENT
                        SetProperty(item.Properties, "6147", dpi);    // WIA_IPS_XRES
                        SetProperty(item.Properties, "6148", dpi);    // WIA_IPS_YRES

                        Type cdType = Type.GetTypeFromProgID("WIA.CommonDialog");
                        dynamic cd = Activator.CreateInstance(cdType);
                        image = cd.ShowTransfer(item, formatGuid, false);
                    }
                }

                if (image == null)
                {
                    SendJson(res, "{\"success\":false,\"error\":\"Scan was cancelled or produced no image\"}");
                    return;
                }

                byte[] bytes = (byte[])image.FileData.BinaryData;
                string base64 = Convert.ToBase64String(bytes);
                int width = (int)image.Width;
                int height = (int)image.Height;

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Scan successful! Dimensions: " + width + "x" + height + ", Size: " + (bytes.Length / 1024) + " KB");
                Console.ResetColor();

                string json = "{\"success\":true,\"width\":" + width + ",\"height\":" + height +
                              ",\"size\":" + bytes.Length + ",\"mime\":\"image/jpeg\",\"data\":\"data:image/jpeg;base64," + base64 + "\"}";
                SendJson(res, json);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Scan failed: " + ex.Message);
                Console.ResetColor();
                SendJson(res, "{\"success\":false,\"error\":" + EscapeJson(ex.Message) + "}");
            }
        }

        private static void SetProperty(dynamic props, string propIdOrName, object val)
        {
            try
            {
                props[propIdOrName].Value = val;
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
                if (endQuote < 0) return def;
                return json.Substring(startQuote + 1, endQuote - startQuote - 1);
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

        private static bool ParseJsonBool(string json, string key, bool def)
        {
            try
            {
                string needle = "\"" + key + "\"";
                int idx = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return def;
                int colon = json.IndexOf(':', idx + needle.Length);
                if (colon < 0) return def;
                int end = json.IndexOfAny(new char[] { ',', '}', '\r', '\n' }, colon + 1);
                string valStr = (end > 0 ? json.Substring(colon + 1, end - colon - 1) : json.Substring(colon + 1)).Trim().ToLowerInvariant();
                if (valStr == "true") return true;
                if (valStr == "false") return false;
                return def;
            }
            catch { return def; }
        }
    }
}
