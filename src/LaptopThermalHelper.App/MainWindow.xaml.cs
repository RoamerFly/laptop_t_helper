using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LaptopThermalHelper.App.Services;
using LaptopThermalHelper.App.ViewModels;
using Serilog;

namespace LaptopThermalHelper.App;

public partial class MainWindow : Window
{
    private readonly DashboardViewModel _viewModel;
    private readonly ThemeService _themeService;
    private readonly DispatcherTimer _sampleTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2),
    };

    public MainWindow(DashboardViewModel viewModel, ThemeService themeService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _themeService = themeService;
        DataContext = viewModel;
        ThemeButton.Content = themeService.IsDark ? "☾  深色" : "☀  浅色";
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        _sampleTimer.Tick += SampleTimer_Tick;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshSafelyAsync();
        _sampleTimer.Start();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _sampleTimer.Stop();
    }

    private async void SampleTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshSafelyAsync();
    }

    private async Task RefreshSafelyAsync()
    {
        try
        {
            await _viewModel.RefreshAsync();
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "硬件采样失败，界面将在下一个周期重试");
        }
    }

    private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
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

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _themeService.Toggle();
        ThemeButton.Content = _themeService.IsDark ? "☾  深色" : "☀  浅色";
        InvalidateVisual();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
}
