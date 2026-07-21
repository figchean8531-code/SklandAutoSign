using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace SklandAutoSign;

internal sealed class AppSettings
{
    public string EncryptedToken { get; set; } = "";
    public string DailyTime { get; set; } = "12:00";
    public bool EnableArknights { get; set; } = true;
    public bool EnableEndfield { get; set; } = true;
}

internal static class AppPaths
{
    public static string DataDirectory => Path.Combine(AppContext.BaseDirectory, "data");
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string LogFile => Path.Combine(DataDirectory, "sign.log");

    public static void EnsureDataDirectory() => Directory.CreateDirectory(DataDirectory);
}

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        AppPaths.EnsureDataDirectory();
        if (!File.Exists(AppPaths.SettingsFile))
            return new AppSettings();

        string json = File.ReadAllText(AppPaths.SettingsFile, Encoding.UTF8);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        AppPaths.EnsureDataDirectory();
        string tempFile = AppPaths.SettingsFile + ".tmp";
        File.WriteAllText(tempFile, JsonSerializer.Serialize(settings, JsonOptions), new UTF8Encoding(false));
        File.Move(tempFile, AppPaths.SettingsFile, true);
    }
}

internal static class SecretProtector
{
    private const int CryptProtectUiForbidden = 0x1;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SklandAutoSign-v1");

    public static string Protect(string plainText)
    {
        byte[] plain = Encoding.UTF8.GetBytes(plainText);
        byte[] encrypted = ProtectOrUnprotect(plain, Entropy, protect: true);
        return Convert.ToBase64String(encrypted);
    }

    public static string Unprotect(string protectedText)
    {
        byte[] encrypted = Convert.FromBase64String(protectedText);
        byte[] plain = ProtectOrUnprotect(encrypted, Entropy, protect: false);
        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] ProtectOrUnprotect(byte[] input, byte[] entropy, bool protect)
    {
        GCHandle inputHandle = default;
        GCHandle entropyHandle = default;
        DataBlob output = default;
        try
        {
            inputHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
            entropyHandle = GCHandle.Alloc(entropy, GCHandleType.Pinned);
            var inputBlob = new DataBlob { Length = input.Length, Data = inputHandle.AddrOfPinnedObject() };
            var entropyBlob = new DataBlob { Length = entropy.Length, Data = entropyHandle.AddrOfPinnedObject() };

            bool ok = protect
                ? CryptProtectData(ref inputBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output);

            if (!ok)
                throw new InvalidOperationException($"Windows 凭证加密失败（{Marshal.GetLastWin32Error()}）。");

            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, output.Length);
            return result;
        }
        finally
        {
            if (output.Data != IntPtr.Zero)
                LocalFree(output.Data);
            if (inputHandle.IsAllocated)
                inputHandle.Free();
            if (entropyHandle.IsAllocated)
                entropyHandle.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string? description, ref DataBlob optionalEntropy,
        IntPtr reserved, IntPtr promptStruct, int flags, out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob optionalEntropy,
        IntPtr reserved, IntPtr promptStruct, int flags, out DataBlob dataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}

internal static class LogWriter
{
    private static readonly object Sync = new();

    public static void Write(string message)
    {
        lock (Sync)
        {
            AppPaths.EnsureDataDirectory();
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            File.AppendAllText(AppPaths.LogFile, line, new UTF8Encoding(false));
        }
    }
}

internal static class TaskSchedulerManager
{
    public const string TaskName = "森空岛游戏每日签到";
    private const string LegacyTaskName = "森空岛终末地每日签到";

    public static (bool Success, string Message) Install(TimeSpan time)
    {
        string exePath = Environment.ProcessPath ?? throw new InvalidOperationException("无法获取程序路径。");
        string? sid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrWhiteSpace(sid))
            throw new InvalidOperationException("无法识别当前 Windows 用户。");

        AppPaths.EnsureDataDirectory();
        string xmlPath = Path.Combine(AppPaths.DataDirectory, "scheduled-task.xml");
        string startBoundary = DateTime.Today.Add(time).ToString("yyyy-MM-dd'T'HH:mm:ss");
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>每天自动完成森空岛明日方舟与终末地签到。</Description>
              </RegistrationInfo>
              <Triggers>
                <CalendarTrigger>
                  <StartBoundary>{startBoundary}</StartBoundary>
                  <Enabled>true</Enabled>
                  <ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>
                </CalendarTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(sid)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT5M</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(exePath)}</Command>
                  <Arguments>--run</Arguments>
                  <WorkingDirectory>{SecurityElement.Escape(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;

        try
        {
            File.WriteAllText(xmlPath, xml, Encoding.Unicode);
            var result = RunSchtasks("/Create", "/TN", TaskName, "/XML", xmlPath, "/F");
            if (result.Success)
                RunSchtasks("/Delete", "/TN", LegacyTaskName, "/F");
            return result;
        }
        finally
        {
            if (File.Exists(xmlPath))
                File.Delete(xmlPath);
        }
    }

    public static (bool Success, string Message) Remove()
    {
        var current = RunSchtasks("/Delete", "/TN", TaskName, "/F");
        var legacy = RunSchtasks("/Delete", "/TN", LegacyTaskName, "/F");
        return current.Success || legacy.Success
            ? (true, current.Success ? current.Message : legacy.Message)
            : current;
    }

    public static bool Exists()
    {
        var result = RunSchtasks("/Query", "/TN", TaskName);
        return result.Success;
    }

    public static TimeSpan? GetScheduledTime()
    {
        var result = RunSchtasks("/Query", "/TN", TaskName, "/XML");
        if (!result.Success || string.IsNullOrWhiteSpace(result.Message))
            return null;

        try
        {
            XDocument document = XDocument.Parse(result.Message);
            XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;
            string? startBoundary = document.Descendants(ns + "StartBoundary").FirstOrDefault()?.Value;
            return DateTime.TryParse(startBoundary, out DateTime start) ? start.TimeOfDay : null;
        }
        catch
        {
            return null;
        }
    }

    private static (bool Success, string Message) RunSchtasks(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.Default,
            StandardErrorEncoding = Encoding.Default
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 Windows 任务计划程序。");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        string message = string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim();
        return (process.ExitCode == 0, message);
    }
}
