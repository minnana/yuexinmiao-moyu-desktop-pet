using Microsoft.Win32;
using System.IO;
using System.Reflection;

namespace YueXinCat.Services;

/// <summary>
/// Reads and updates the current-user Windows startup entry. No elevation is
/// required because the value lives under HKCU. During development, the entry
/// invokes dotnet with the built YueXinCat.dll; a published build invokes the
/// standalone YueXinCat.exe directly.
/// </summary>
public sealed class StartupService
{
    public const string ValueName = "YueXinCat";
    public const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

    public bool IsEnabled => TryGetCommand(out _);

    public bool TryGetCommand(out string? command)
    {
        command = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            command = key?.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(command);
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }

    public bool SetEnabled(bool enabled, string? applicationPath = null)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
                return false;

            if (enabled)
            {
                var command = BuildLaunchCommand(applicationPath);
                key.SetValue(ValueName, command, RegistryValueKind.String);
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }

    public string BuildLaunchCommand(string? applicationPath = null)
    {
        var processPath = applicationPath;
        if (string.IsNullOrWhiteSpace(processPath))
            processPath = Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(processPath))
            throw new InvalidOperationException("无法确定应用程序路径。");

        if (IsDotnetHost(processPath))
        {
            // Assembly.Location is empty for single-file deployments. AppContext
            // points at the build output directory in the dotnet development case.
            var assemblyName = Assembly.GetEntryAssembly()?.GetName().Name ?? "YueXinCat";
            var managedAssembly = Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll");
            if (!File.Exists(managedAssembly))
                throw new InvalidOperationException("调试启动时找不到 YueXinCat.dll。");
            return $"{Quote(processPath)} {Quote(managedAssembly)}";
        }

        return Quote(Path.GetFullPath(processPath));
    }

    private static bool IsDotnetHost(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.Equals("dotnet", StringComparison.OrdinalIgnoreCase);
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
