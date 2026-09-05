using System.Windows;
using System.Windows.Input;
using LaptopThermalHelper.App.Services;
using LaptopThermalHelper.App.ViewModels;

namespace LaptopThermalHelper.App.Views;

public partial class FloatingWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private readonly ThemeService _themeService;
    private readonly Action _showMainWindowAction;
    private readonly Action _exitApplicationAction;
    private bool _positionLoaded;

    public FloatingWindow(
        ShellViewModel viewModel,
        ThemeService themeService,
        Action showMainWindowAction,
        Action exitApplicationAction)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _themeService = themeService;
        _showMainWindowAction = showMainWindowAction;
        _exitApplicationAction = exitApplicationAction;

        DataContext = _viewModel;
        Loaded += FloatingWindow_Loaded;
    }

    private void FloatingWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_positionLoaded)
        {
            return;
        }

        _positionLoaded = true;
        ApplySavedPosition();
    }

    private void ApplySavedPosition()
    {
        double screenWidth = SystemParameters.WorkArea.Width;
        double screenHeight = SystemParameters.WorkArea.Height;
        double defaultLeft = Math.Max(20, screenWidth - ActualWidth - 20);
        double defaultTop = 40;

        // Position will be refined if values exist in settings
        Left = defaultLeft;
        Top = defaultTop;
    }

    public void RestorePosition(double left, double top)
    {
        if (left >= 0 && left < SystemParameters.VirtualScreenWidth &&
            top >= 0 && top < SystemParameters.VirtualScreenHeight)
        {
            Left = left;
            Top = top;
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _showMainWindowAction();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
                _ = _viewModel.UpdateFloatingWindowPositionAsync(Left, Top);
            }
            catch
            {
                // DragMove may fail if mouse state changes rapidly
            }
        }
    }

    private void MenuShowMainWindow_Click(object sender, RoutedEventArgs e) =>
        _showMainWindowAction();

    private void MenuToggleTheme_Click(object sender, RoutedEventArgs e) =>
        _themeService.Toggle();

    private void MenuHide_Click(object sender, RoutedEventArgs e) =>
        _viewModel.ShowFloatingWindow = false;

    private void MenuExit_Click(object sender, RoutedEventArgs e) =>
        _exitApplicationAction();
}
