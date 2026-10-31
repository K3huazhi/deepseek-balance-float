// DeepSeek 余额悬浮窗 · Lite 启动器
// ------------------------------------------------------------------
// 极简启动器（约 120 KB）：本身不含 Python/Qt 运行时，
// 而是把内嵌的应用源码释放到临时目录，再用本机已安装的
// Python + PySide6 拉起应用。
//
// 找不到解释器时会让用户手动选择 python.exe，并把路径记到
// HKCU\Software\DeepSeekBalance\python，下次直接用。
//
// 编译（Windows 自带的 .NET Framework 编译器即可）：
//   csc /nologo /target:winexe /optimize+ /codepage:65001 ^
//       /win32icon:icon.ico ^
//       /r:System.Windows.Forms.dll ^
//       /resource:balance_float.py,balance_float.py ^
//       /out:DeepSeek-Balance-Float-Lite.exe launcher.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

static class Program
{
    const string AppName = "DeepSeek 余额悬浮窗";
    const string SrcName = "balance_float.py";
    const string TempSub = "DeepSeekBalanceFloat";
    const string RegPath = @"Software\DeepSeekBalance";

    const uint MB_ICONERROR = 0x10;
    const uint MB_YESNO = 0x04;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    static void Msg(string text, uint type = MB_ICONERROR)
    {
        try { MessageBoxW(IntPtr.Zero, text, AppName + " · Lite", type); }
        catch { }
    }

    static int Ask(string text)
    {
        try { return MessageBoxW(IntPtr.Zero, text, AppName + " · Lite", MB_YESNO | MB_ICONERROR); }
        catch { return 7; }
    }

    // ---------- 记住用户手动选择的解释器 ----------
    static string LoadSavedPython()
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RegPath))
            {
                if (k == null) return null;
                string p = k.GetValue("python") as string;
                if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
            }
        }
        catch { }
        return null;
    }

    static void SavePython(string path)
    {
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RegPath))
                if (k != null) k.SetValue("python", path, RegistryValueKind.String);
        }
        catch { }
    }

    // ---------- 释放内嵌源码 ----------
    static string ExtractSource()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream(SrcName))
            {
                if (s == null) return null;
                string dir = Path.Combine(Path.GetTempPath(), TempSub);
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, SrcName);
                using (var f = File.Create(path)) s.CopyTo(f);
                return path;
            }
        }
        catch { return null; }
    }

    static void AddIfExists(List<string> list, string dir, string exe)
    {
        try
        {
            if (string.IsNullOrEmpty(dir)) return;
            string p = Path.Combine(dir, exe);
            if (File.Exists(p)) list.Add(p);
        }
        catch { }
    }

    static List<string> Candidates()
    {
        var list = new List<string>();

        string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(';'))
        {
            string d = dir.Trim().Trim('"');
            AddIfExists(list, d, "pythonw.exe");
            AddIfExists(list, d, "python.exe");
            AddIfExists(list, d, "py.exe");
        }

        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default))
                using (var root = baseKey.OpenSubKey(@"SOFTWARE\Python\PythonCore"))
                {
                    if (root == null) continue;
                    foreach (var ver in root.GetSubKeyNames())
                    {
                        using (var ip = root.OpenSubKey(ver + @"\InstallPath"))
                        {
                            if (ip == null) continue;
                            string d = ip.GetValue("") as string;
                            AddIfExists(list, d, "pythonw.exe");
                            AddIfExists(list, d, "python.exe");
                        }
                    }
                }
            }
            catch { }
        }

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var roots = new[] { Path.Combine(local, @"Programs\Python"), pf, pf86, @"C:\" };
        foreach (var r in roots)
        {
            try
            {
                if (string.IsNullOrEmpty(r) || !Directory.Exists(r)) continue;
                foreach (var d in Directory.GetDirectories(r, "Python3*"))
                {
                    AddIfExists(list, d, "pythonw.exe");
                    AddIfExists(list, d, "python.exe");
                }
            }
            catch { }
        }

        return list;
    }

    static bool IsPyLauncher(string exe)
    {
        return string.Equals(Path.GetFileName(exe), "py.exe", StringComparison.OrdinalIgnoreCase);
    }

    static bool HasPySide(string exe)
    {
        try
        {
            var psi = new ProcessStartInfo(exe);
            psi.Arguments = (IsPyLauncher(exe) ? "-3 -c " : "-c ") + "\"import PySide6\"";
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (var p = Process.Start(psi))
            {
                if (!p.WaitForExit(30000))
                {
                    try { p.Kill(); } catch { }
                    return false;
                }
                return p.ExitCode == 0;
            }
        }
        catch { return false; }
    }

    static bool Launch(string exe, string script)
    {
        try
        {
            var psi = new ProcessStartInfo(exe);
            psi.Arguments = (IsPyLauncher(exe) ? "-3 " : "") + "\"" + script + "\"";
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.WorkingDirectory = Path.GetDirectoryName(script);
            psi.EnvironmentVariables["DSH_LITE_EXE"] = Assembly.GetExecutingAssembly().Location;
            Process.Start(psi);
            return true;
        }
        catch { return false; }
    }

    static string PickPython()
    {
        try
        {
            var dlg = new System.Windows.Forms.OpenFileDialog();
            dlg.Title = "请选择 python.exe 或 pythonw.exe";
            dlg.Filter = "Python 解释器 (python*.exe)|python*.exe|所有程序 (*.exe)|*.exe";
            dlg.CheckFileExists = true;
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                return dlg.FileName;
        }
        catch { }
        return null;
    }

    static string TryPick(string script)
    {
        string pick = PickPython();
        if (string.IsNullOrEmpty(pick)) return null;
        if (!HasPySide(pick))
        {
            Msg("选中的 Python 不能 import PySide6。\n\n" +
                "请先在这个环境里安装依赖：\n" +
                "    pip install PySide6-Essentials");
            return null;
        }
        SavePython(pick);
        return Launch(pick, script) ? pick : null;
    }

    [STAThread]
    static int Main()
    {
        string script = ExtractSource();
        if (script == null)
        {
            Msg("程序文件缺失，请重新下载 Lite 版。");
            return 1;
        }

        // 1) 之前手动指定的解释器
        string saved = LoadSavedPython();
        if (!string.IsNullOrEmpty(saved) && HasPySide(saved) && Launch(saved, script))
            return 0;

        // 2) 自动探测
        var tried = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var exe in Candidates())
        {
            string key;
            try { key = Path.GetFullPath(exe); } catch { continue; }
            if (!seen.Add(key)) continue;
            tried.Add(exe);
            if (!HasPySide(exe)) continue;
            if (Launch(exe, script))
            {
                SavePython(exe);
                return 0;
            }
        }

        // 3) 手动指定
        int answer = Ask(
            "没有自动找到可用的 Python 环境。\n\n" +
            "Lite 版体积小，是因为它复用本机已有的 Python 与 PySide6，\n" +
            "需要：Python 3.9+ 且已安装 PySide6-Essentials。\n\n" +
            "是否现在手动选择一个 python.exe / pythonw.exe？\n" +
            "（如果还没装环境，选“否”查看安装说明）");
        if (answer == 6)
        {
            if (TryPick(script) != null) return 0;
            // 用户取消或选错，继续给说明
        }

        var sb = new StringBuilder();
        sb.AppendLine("Lite 版需要本机已有 Python 环境。");
        sb.AppendLine();
        sb.AppendLine("安装步骤：");
        sb.AppendLine("  1. 到 https://www.python.org/downloads/ 安装 Python 3.9+");
        sb.AppendLine("     （安装时勾选 Add python.exe to PATH）");
        sb.AppendLine("  2. 打开命令行执行：");
        sb.AppendLine("         pip install PySide6-Essentials");
        sb.AppendLine();
        sb.AppendLine("之后重新运行本程序，或再次运行时手动选择 python.exe。");
        sb.AppendLine();
        sb.AppendLine("不想配置环境的话，请改用完整版（内置运行时，双击即用）。");
        if (tried.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("已尝试：");
            int n = Math.Min(6, tried.Count);
            for (int i = 0; i < n; i++) sb.AppendLine("  · " + tried[i]);
        }
        Msg(sb.ToString());
        return 1;
    }
}
