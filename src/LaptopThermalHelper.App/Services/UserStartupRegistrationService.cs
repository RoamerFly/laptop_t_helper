using System.IO;
using System.Security;
using Microsoft.Win32;

namespace LaptopThermalHelper.App.Services;

public sealed record StartupRegistrationResult(bool Succeeded, string Message);

public interface IUserStartupRegistrationService
{
    Task<StartupRegistrationResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);

    Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default);
}

public sealed class UserStartupRegistrationService : IUserStartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LaptopThermalHelper";

    public Task<StartupRegistrationResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            RegistryKey? registryKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
            if (registryKey is null)
            {
                return Task.FromResult(new StartupRegistrationResult(false, "无法打开当前用户启动项注册表项。"));
            }

            using RegistryKey key = registryKey;

            if (!enabled)
            {
                key.DeleteValue(ValueName, false);
                return Task.FromResult(new StartupRegistrationResult(true, "已关闭当前用户开机启动。"));
            }

            string? executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return Task.FromResult(new StartupRegistrationResult(
                    false,
                    "无法确定当前应用程序路径，未写入开机启动项。"));
            }

            key.SetValue(ValueName, $"\"{executablePath.Replace("\"", string.Empty)}\"", RegistryValueKind.String);
            return Task.FromResult(new StartupRegistrationResult(true, "已为当前用户启用开机启动。"));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException)
        {
            return Task.FromResult(new StartupRegistrationResult(false, $"开机启动设置失败：{exception.Message}"));
        }
    }

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return Task.FromResult(key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value));
    }
}
