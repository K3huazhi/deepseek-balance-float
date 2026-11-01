// DeepSeek 余额悬浮窗 · 非托管依赖层（Win32 / 注册表 / 凭据管理器 / HTTP / 统计）
// .NET Framework 4.8 + WPF，无第三方依赖
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Dsb
{
    // ---------------------------------------------------------------- Win32
    internal static class Native
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOPMOST = 0x00000008;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;

        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWA_BORDER_COLOR = 34;
        public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS { public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight; }

        [DllImport("dwmapi.dll")]
        public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        public static void ExtendGlass(IntPtr hwnd)
        {
            try
            {
                MARGINS m;
                m.cxLeftWidth = -1; m.cxRightWidth = -1; m.cyTopHeight = -1; m.cyBottomHeight = -1;
                DwmExtendFrameIntoClientArea(hwnd, ref m);
            }
            catch { }
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        public static int Build
        {
            get
            {
                try { return Environment.OSVersion.Version.Build; }
                catch { return 0; }
            }
        }

        public static bool MicaSupported { get { return Build >= 22000; } }

        public static void ApplyBackdrop(IntPtr hwnd, bool dark)
        {
            if (hwnd == IntPtr.Zero) return;
            int v = dark ? 1 : 0;
            Try(() => DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, 4));
            int round = 2;
            Try(() => DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4));
            int border = unchecked((int)0xFFFFFFFE);
            Try(() => DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, 4));
            if (Build >= 22621)
            {
                int mica = 2;
                Try(() => DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref mica, 4));
            }
            else if (Build >= 22000)
            {
                int legacy = 1;
                Try(() => DwmSetWindowAttribute(hwnd, 1029, ref legacy, 4));
            }
        }

        public static void SetTopmost(IntPtr hwnd, bool on)
        {
            SetWindowPos(hwnd, on ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        public static bool IsTopmost(IntPtr hwnd)
        {
            return (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
        }

        private static void Try(Action a)
        {
            try { a(); } catch { }
        }

        public static void Error(string text)
        {
            try { MessageBoxW(IntPtr.Zero, text, "DeepSeek 余额悬浮窗", 0x10); }
            catch { }
        }
    }

    // ------------------------------------------------------------ 凭据管理器
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    internal static class Cred
    {
        public const string Target = "DeepSeekBalance:api_key";
        private const uint CRED_TYPE_GENERIC = 1;
        private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWriteW(ref CREDENTIAL credential, uint flags);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr buffer);

        public static string Read()
        {
            IntPtr p = IntPtr.Zero;
            try
            {
                if (!CredReadW(Target, CRED_TYPE_GENERIC, 0, out p) || p == IntPtr.Zero) return "";
                CREDENTIAL c = (CREDENTIAL)Marshal.PtrToStructure(p, typeof(CREDENTIAL));
                if (c.CredentialBlobSize == 0) return "";
                byte[] raw = new byte[c.CredentialBlobSize];
                Marshal.Copy(c.CredentialBlob, raw, 0, raw.Length);
                return Encoding.Unicode.GetString(raw).Trim();
            }
            catch { return ""; }
            finally { if (p != IntPtr.Zero) CredFree(p); }
        }

        public static bool Write(string secret)
        {
            byte[] blob = Encoding.Unicode.GetBytes(secret);
            IntPtr buf = Marshal.AllocHGlobal(Math.Max(blob.Length, 2));
            try
            {
                Marshal.Copy(blob, 0, buf, blob.Length);
                CREDENTIAL c = new CREDENTIAL();
                c.Type = CRED_TYPE_GENERIC;
                c.TargetName = Target;
                c.Comment = "DeepSeek 余额悬浮窗";
                c.CredentialBlobSize = (uint)blob.Length;
                c.CredentialBlob = buf;
                c.Persist = CRED_PERSIST_LOCAL_MACHINE;
                c.UserName = "DeepSeek";
                return CredWriteW(ref c, 0);
            }
            catch { return false; }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }

    // --------------------------------------------------------------- 设置存储
    internal sealed class Store
    {
        private const string PATH = @"Software\DeepSeekBalance";

        public string Get(string name, string fallback)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(PATH))
                {
                    if (k == null) return fallback;
                    object v = k.GetValue(name);
                    return v == null ? fallback : Convert.ToString(v);
                }
            }
            catch { return fallback; }
        }

        public bool GetBool(string name, bool fallback)
        {
            string s = Get(name, null);
            if (s == null) return fallback;
            return s == "1";
        }

        public void Set(string name, string value)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(PATH))
                    if (k != null) k.SetValue(name, value, RegistryValueKind.String);
            }
            catch { }
        }

        public void SetBool(string name, bool value) { Set(name, value ? "1" : "0"); }
    }

    // ------------------------------------------------------------- Key 与统计
    internal sealed class KeyItem
    {
        public string Key = "";
        public bool Show = true;
        public string Name = "";
    }

    internal static class Keys
    {
        public const int MaxKeys = 10;
        public const int MaxShow = 3;

        public static string DefaultName(string key)
        {
            if (string.IsNullOrEmpty(key)) return "未命名";
            string body = key.StartsWith("sk-", StringComparison.Ordinal) ? key.Substring(3) : key;
            return body.Length >= 6 ? body.Substring(0, 6) : (body.Length > 0 ? body : "未命名");
        }

        public static string Mask(string key)
        {
            key = (key ?? "").Trim();
            if (key.Length <= 12) return key;
            int cut = (key.Length - 6) / 2;
            return key.Substring(0, cut) + "******" + key.Substring(cut + 6);
        }

        public static string Uid(string key)
        {
            using (SHA1 sha = SHA1.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(key ?? ""));
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < 6; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        public static List<KeyItem> Load()
        {
            List<KeyItem> list = new List<KeyItem>();
            string raw = Cred.Read();
            if (string.IsNullOrEmpty(raw)) return list;
            if (raw.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                try
                {
                    JavaScriptSerializer js = new JavaScriptSerializer();
                    object parsed = js.DeserializeObject(raw);
                    System.Collections.IList arr = parsed as System.Collections.IList;
                    if (arr != null)
                    {
                        foreach (object o in arr)
                        {
                            Dictionary<string, object> d = o as Dictionary<string, object>;
                            if (d == null) continue;
                            string k = d.ContainsKey("k") ? Convert.ToString(d["k"]).Trim() : "";
                            if (k.Length == 0) continue;
                            bool show = true;
                            if (d.ContainsKey("show")) show = Convert.ToBoolean(d["show"]);
                            string nm = d.ContainsKey("name") ? Convert.ToString(d["name"]).Trim() : "";
                            if (nm.Length == 0) nm = DefaultName(k);
                            KeyItem it = new KeyItem();
                            it.Key = k; it.Show = show; it.Name = nm;
                            list.Add(it);
                            if (list.Count >= MaxKeys) break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    try { System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsb_err.txt"),
                        ex.GetType().Name + ": " + ex.Message + "\nRAW=" + raw); } catch { }
                }
                return list;
            }
            KeyItem legacy = new KeyItem();
            legacy.Key = raw.Trim();
            legacy.Show = true;
            legacy.Name = DefaultName(legacy.Key);
            list.Add(legacy);
            return list;
        }

        public static bool Save(List<KeyItem> keys)
        {
            List<object> arr = new List<object>();
            foreach (KeyItem k in keys)
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["k"] = k.Key;
                d["show"] = k.Show;
                d["name"] = string.IsNullOrEmpty(k.Name) ? DefaultName(k.Key) : k.Name;
                arr.Add(d);
            }
            JavaScriptSerializer js = new JavaScriptSerializer();
            return Cred.Write(js.Serialize(arr));
        }
    }

    internal sealed class KeyStat
    {
        public Dictionary<string, double> Days = new Dictionary<string, double>();
        public double? Last = null;
    }

    internal sealed class Stats
    {
        private readonly Store _store;
        private Dictionary<string, KeyStat> _map = new Dictionary<string, KeyStat>();

        public Stats(Store store)
        {
            _store = store;
            Load();
        }

        public KeyStat For(string key)
        {
            string uid = Keys.Uid(key);
            KeyStat st;
            if (!_map.TryGetValue(uid, out st))
            {
                st = new KeyStat();
                _map[uid] = st;
            }
            return st;
        }

        public void Record(KeyStat st, double total)
        {
            if (st.Last.HasValue)
            {
                double spent = st.Last.Value - total;
                if (spent > 0.0005)
                {
                    string day = DateTime.Today.ToString("yyyy-MM-dd");
                    double cur;
                    st.Days.TryGetValue(day, out cur);
                    st.Days[day] = Math.Round(cur + spent, 4);
                }
            }
            st.Last = total;
        }

        public double Today(KeyStat st)
        {
            double v;
            st.Days.TryGetValue(DateTime.Today.ToString("yyyy-MM-dd"), out v);
            return Math.Round(v, 2);
        }

        public double[] Week(KeyStat st)
        {
            double[] arr = new double[7];
            for (int i = 0; i < 7; i++)
            {
                string day = DateTime.Today.AddDays(i - 6).ToString("yyyy-MM-dd");
                double v;
                st.Days.TryGetValue(day, out v);
                arr[i] = Math.Round(v, 2);
            }
            return arr;
        }

        public void Load()
        {
            _map = new Dictionary<string, KeyStat>();
            string raw = _store.Get("stats", "");
            if (string.IsNullOrEmpty(raw)) return;
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                Dictionary<string, object> root = js.DeserializeObject(raw) as Dictionary<string, object>;
                if (root == null) return;
                // 旧格式（单 Key）：{"days": {...}, "last_balance": x}
                if (root.ContainsKey("days") || root.ContainsKey("last_balance"))
                {
                    KeyStat legacy = new KeyStat();
                    Dictionary<string, object> days = root.ContainsKey("days") ? root["days"] as Dictionary<string, object> : null;
                    if (days != null)
                        foreach (KeyValuePair<string, object> kv in days)
                        {
                            double v;
                            if (double.TryParse(Convert.ToString(kv.Value), NumberStyles.Any, CultureInfo.InvariantCulture, out v))
                                legacy.Days[kv.Key] = v;
                        }
                    if (root.ContainsKey("last_balance") && root["last_balance"] != null)
                        legacy.Last = Convert.ToDouble(root["last_balance"]);
                    List<KeyItem> keys = Keys.Load();
                    if (keys.Count > 0) _map[Keys.Uid(keys[0].Key)] = legacy;
                    return;
                }
                foreach (KeyValuePair<string, object> kv in root)
                {
                    Dictionary<string, object> d = kv.Value as Dictionary<string, object>;
                    if (d == null) continue;
                    KeyStat st = new KeyStat();
                    Dictionary<string, object> days = d.ContainsKey("days") ? d["days"] as Dictionary<string, object> : null;
                    if (days != null)
                        foreach (KeyValuePair<string, object> dv in days)
                        {
                            double v;
                            if (double.TryParse(Convert.ToString(dv.Value), NumberStyles.Any, CultureInfo.InvariantCulture, out v))
                                st.Days[dv.Key] = v;
                        }
                    if (d.ContainsKey("last") && d["last"] != null)
                        st.Last = Convert.ToDouble(d["last"]);
                    _map[kv.Key] = st;
                }
            }
            catch { }
        }

        public void Save()
        {
            try
            {
                string cutoff = DateTime.Today.AddDays(-90).ToString("yyyy-MM-dd");
                Dictionary<string, object> root = new Dictionary<string, object>();
                foreach (KeyValuePair<string, KeyStat> kv in _map)
                {
                    Dictionary<string, object> days = new Dictionary<string, object>();
                    foreach (KeyValuePair<string, double> d in kv.Value.Days)
                        if (string.CompareOrdinal(d.Key, cutoff) >= 0) days[d.Key] = d.Value;
                    Dictionary<string, object> item = new Dictionary<string, object>();
                    item["days"] = days;
                    item["last"] = kv.Value.Last.HasValue ? (object)kv.Value.Last.Value : null;
                    root[kv.Key] = item;
                }
                JavaScriptSerializer js = new JavaScriptSerializer();
                _store.Set("stats", js.Serialize(root));
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------- 接口
    internal sealed class BalanceInfo
    {
        public string Symbol = "¥";
        public double Total;
        public double Granted;
        public double Topped;
        public bool Available = true;
    }

    internal sealed class ApiException : Exception
    {
        public readonly int Status;
        public ApiException(int status, string message) : base(message) { Status = status; }
    }

    internal static class Api
    {
        public static BalanceInfo Fetch(string key, string url)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Headers["Authorization"] = "Bearer " + key;
            req.Accept = "application/json";
            req.Timeout = 15000;
            req.ReadWriteTimeout = 15000;
            req.UserAgent = "DeepSeekBalanceFloat/1.2";
            string body;
            try
            {
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    body = sr.ReadToEnd();
            }
            catch (WebException ex)
            {
                HttpWebResponse resp = ex.Response as HttpWebResponse;
                if (resp != null)
                {
                    int code = (int)resp.StatusCode;
                    resp.Close();
                    if (code == 401) throw new ApiException(401, "Key 无效或已被删除");
                    if (code == 402) throw new ApiException(402, "账户余额不足");
                    if (code == 429) throw new ApiException(429, "请求过于频繁");
                    throw new ApiException(code, "接口返回 " + code);
                }
                throw new ApiException(0, "网络不可用");
            }

            JavaScriptSerializer js = new JavaScriptSerializer();
            Dictionary<string, object> root = js.DeserializeObject(body) as Dictionary<string, object>;
            if (root == null) throw new ApiException(0, "接口返回异常");
            object infosObj;
            if (!root.TryGetValue("balance_infos", out infosObj)) throw new ApiException(0, "没有余额信息");
            System.Collections.IList infos = infosObj as System.Collections.IList;
            if (infos == null || infos.Count == 0) throw new ApiException(0, "没有余额信息");
            Dictionary<string, object> it = infos[0] as Dictionary<string, object>;
            if (it == null) throw new ApiException(0, "没有余额信息");

            BalanceInfo bi = new BalanceInfo();
            string cur = it.ContainsKey("currency") ? Convert.ToString(it["currency"]) : "CNY";
            bi.Symbol = cur == "CNY" ? "¥" : cur + " ";
            bi.Total = Num(it, "total_balance");
            bi.Granted = Num(it, "granted_balance");
            bi.Topped = Num(it, "topped_up_balance");
            object avail;
            if (root.TryGetValue("is_available", out avail) && avail != null)
                bi.Available = Convert.ToBoolean(avail);
            return bi;
        }

        private static double Num(Dictionary<string, object> d, string name)
        {
            object v;
            if (!d.TryGetValue(name, out v) || v == null) return 0;
            double r;
            double.TryParse(Convert.ToString(v), NumberStyles.Any, CultureInfo.InvariantCulture, out r);
            return r;
        }
    }

    // ------------------------------------------------------------ 开机自启
    internal static class Autostart
    {
        private const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RUN_NAME = "DeepSeekBalanceFloat";

        public static string LegacyBat
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Windows\Start Menu\Programs\Startup");
                return Path.Combine(dir, "DeepSeek余额悬浮窗.bat");
            }
        }

        public static bool Enabled
        {
            get
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RUN_KEY))
                        return k != null && k.GetValue(RUN_NAME) != null;
                }
                catch { return false; }
            }
        }

        public static bool Toggle(out string error)
        {
            error = null;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RUN_KEY))
                {
                    if (k == null) { error = "无法打开注册表"; return false; }
                    if (k.GetValue(RUN_NAME) != null)
                    {
                        k.DeleteValue(RUN_NAME, false);
                        TryDeleteLegacy();
                        return false;
                    }
                    string exe = Process.GetCurrentProcess().MainModule.FileName;
                    k.SetValue(RUN_NAME, "\"" + exe + "\"", RegistryValueKind.String);
                    TryDeleteLegacy();
                    return true;
                }
            }
            catch (Exception e) { error = e.Message; return false; }
        }

        private static void TryDeleteLegacy()
        {
            try
            {
                string p = LegacyBat;
                if (File.Exists(p)) File.Delete(p);
            }
            catch { }
        }
    }
}
