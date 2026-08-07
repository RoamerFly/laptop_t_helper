using System.Drawing;
using System.IO;
using System.Windows;
using LaptopThermalHelper.Core.Domain;
using Forms = System.Windows.Forms;

namespace LaptopThermalHelper.App.Services;

public sealed record TrayStatus(
    ThermalLevel SystemLevel,
    double? CpuTemperature,
    double? GpuTemperature,
    double? StorageTemperature)
{
    public string ToolTipText
    {
        get
        {
            string text = $"CPU {Format(CpuTemperature)} | GPU {Format(GpuTemperature)} | SSD {Format(StorageTemperature)}";
            return $"{text}\n整机状态：{StatusText(SystemLevel)}";
        }
    }

    private static string Format(double? value) => value is double temperature
        ? $"{temperature:0}°C"
        : "--";

    private static string StatusText(ThermalLevel level) => level switch
    {
        ThermalLevel.Normal => "正常",
        ThermalLevel.Elevated => "温度偏高",
        ThermalLevel.High => "温度过高",
        ThermalLevel.Critical => "严重过热",
        _ => "传感器未就绪",
    };
}

public interface ITrayIconService : IDisposable
{
    event EventHandler? ShowRequested;

    event EventHandler? ExitRequested;

    bool IsAvailable { get; }

    void Initialize();

    void UpdateStatus(TrayStatus status);

    bool ShowNotification(string title, string message, bool isCritical);
}

public sealed class WindowsTrayIconService : ITrayIconService
{
    private Forms.NotifyIcon? _notifyIcon;
    private bool _disposed;

    public event EventHandler? ShowRequested;

    public event EventHandler? ExitRequested;

    public bool IsAvailable => _notifyIcon is not null && !_disposed;

    public void Initialize()
    {
        if (_disposed || _notifyIcon is not null)
        {
            return;
        }

        try
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("显示主窗口", null, (_, _) => RaiseOnUiThread(ShowRequested));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("退出", null, (_, _) => RaiseOnUiThread(ExitRequested));

            _notifyIcon = new Forms.NotifyIcon
            {
                ContextMenuStrip = menu,
                Icon = LoadApplicationIcon(),
                Text = "笔记本温控助手\n正在初始化传感器…",
                Visible = true,
            };
            _notifyIcon.DoubleClick += (_, _) => RaiseOnUiThread(ShowRequested);
        }
        catch (Exception)
        {
            Dispose();
        }
    }

    public void UpdateStatus(TrayStatus status)
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Text = TrimToolTip(status.ToolTipText);
    }

    public bool ShowNotification(string title, string message, bool isCritical)
    {
        if (_notifyIcon is null)
        {
            return false;
        }

        try
        {
            _notifyIcon.ShowBalloonTip(
                6_000,
                title,
                message,
                isCritical ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }

    private static Icon LoadApplicationIcon()
    {
        string? executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath))
        {
            using Icon? icon = Icon.ExtractAssociatedIcon(executablePath);
            if (icon is not null)
            {
                return (Icon)icon.Clone();
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    private static string TrimToolTip(string value) => value.Length <= 63 ? value : value[..63];

    private static void RaiseOnUiThread(EventHandler? handler)
    {
        if (handler is null)
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(handler, null, EventArgs.Empty);
            return;
        }

        handler(null, EventArgs.Empty);
    }
}
