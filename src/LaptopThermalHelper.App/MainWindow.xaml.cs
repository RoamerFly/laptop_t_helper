using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LaptopThermalHelper.App.Services;
using LaptopThermalHelper.App.ViewModels;
using Serilog;

namespace LaptopThermalHelper.App;

public partial class MainWindow : Window
{
    private const int WindowMessageGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const double DefaultMinWindowWidth = 1280;
    private const double DefaultMinWindowHeight = 720;
    private const double PreferredWindowWidth = 1440;
    private const double PreferredWindowHeight = 900;
    private const double InitialWorkAreaRatio = 0.94;
    private readonly ShellViewModel _viewModel;
    private readonly ThemeService _themeService;
    private readonly DispatcherTimer _sampleTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _initialBoundsApplied;

    public MainWindow(ShellViewModel viewModel, ThemeService themeService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _themeService = themeService;
        DataContext = viewModel;
        UpdateThemeButton();
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        _sampleTimer.Tick += SampleTimer_Tick;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr windowHandle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(windowHandle)?.AddHook(WindowMessageHook);
        ApplyInitialBounds(windowHandle);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshSafelyAsync();
        _sampleTimer.Start();
    }

    private void MainWindow_Closed(object? sender, EventArgs e) => _sampleTimer.Stop();

    private async void SampleTimer_Tick(object? sender, EventArgs e) => await RefreshSafelyAsync();

    private async Task RefreshSafelyAsync()
    {
        try
        {
            await _viewModel.Dashboard.RefreshAsync();
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "硬件采样失败，界面将在下一个周期重试");
        }
    }

    private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || IsInteractiveSource(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        DragMove();
    }

    private static bool IsInteractiveSource(DependencyObject? source)
    {
        for (FrameworkElement? current = source as FrameworkElement; current is not null; current = current.Parent as FrameworkElement)
        {
            if (current is ButtonBase or ToggleButton or TextBox or ComboBox or ListBoxItem)
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyInitialBounds(IntPtr windowHandle)
    {
        if (_initialBoundsApplied)
        {
            return;
        }

        IntPtr monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        uint dpi = GetDpiForWindow(windowHandle);
        double scale = Math.Max(1, dpi / 96d);
        double workAreaWidth = (monitorInfo.WorkArea.Right - monitorInfo.WorkArea.Left) / scale;
        double workAreaHeight = (monitorInfo.WorkArea.Bottom - monitorInfo.WorkArea.Top) / scale;
        if (workAreaWidth <= 0 || workAreaHeight <= 0)
        {
            return;
        }

        // Keep the normal 1280×720 minimum when it fits, but scale it down before
        // Windows clamps the first window outside a high-DPI work area.
        MinWidth = Math.Min(DefaultMinWindowWidth, Math.Max(1, workAreaWidth * 0.9));
        MinHeight = Math.Min(DefaultMinWindowHeight, Math.Max(1, workAreaHeight * 0.9));
        Width = Math.Max(MinWidth, Math.Min(PreferredWindowWidth, workAreaWidth * InitialWorkAreaRatio));
        Height = Math.Max(MinHeight, Math.Min(PreferredWindowHeight, workAreaHeight * InitialWorkAreaRatio));
        Left = (monitorInfo.WorkArea.Left / scale) + ((workAreaWidth - Width) / 2);
        Top = (monitorInfo.WorkArea.Top / scale) + ((workAreaHeight - Height) / 2);
        _initialBoundsApplied = true;
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _themeService.Toggle();
        UpdateThemeButton();
        InvalidateVisual();
    }

    private void UpdateThemeButton() => ThemeButton.Content = _themeService.IsDark ? "☾  深色" : "☀  浅色";

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private static IntPtr WindowMessageHook(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WindowMessageGetMinMaxInfo)
        {
            return IntPtr.Zero;
        }

        IntPtr monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return IntPtr.Zero;
        }

        MinMaxInfo minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        minMaxInfo.MaxPosition.X = monitorInfo.WorkArea.Left - monitorInfo.MonitorArea.Left;
        minMaxInfo.MaxPosition.Y = monitorInfo.WorkArea.Top - monitorInfo.MonitorArea.Top;
        minMaxInfo.MaxSize.X = monitorInfo.WorkArea.Right - monitorInfo.WorkArea.Left;
        minMaxInfo.MaxSize.Y = monitorInfo.WorkArea.Bottom - monitorInfo.WorkArea.Top;
        Marshal.StructureToPtr(minMaxInfo, lParam, false);
        handled = true;
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRectangle MonitorArea;
        public NativeRectangle WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
