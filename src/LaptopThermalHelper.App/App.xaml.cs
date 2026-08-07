using System.Windows;
using LaptopThermalHelper.App.Services;
using LaptopThermalHelper.App.ViewModels;
using LaptopThermalHelper.Application.Hardware;
using LaptopThermalHelper.Application.History;
using LaptopThermalHelper.Application.Monitoring;
using LaptopThermalHelper.Hardware.Lhm;
using LaptopThermalHelper.Infrastructure.History;
using LaptopThermalHelper.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace LaptopThermalHelper.App;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Log.Logger = LoggingBootstrapper.CreateLogger();
        bool useRealHardware = e.Args.Contains("--real-hardware", StringComparer.OrdinalIgnoreCase);

        _host = Host.CreateDefaultBuilder(e.Args)
            .UseSerilog()
            .ConfigureServices(services =>
            {
                services.AddSingleton<ThemeService>();
                services.AddSingleton<IHardwareMonitorProvider>(_ => useRealHardware
                    ? new LhmHardwareMonitorProvider()
                    : new FakeHardwareMonitorProvider());
                services.AddSingleton<ITemperatureHistoryStore, CsvTemperatureHistoryStore>();
                services.AddSingleton<MonitoringCoordinator>();
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        _host.Start();
        if (e.Args.Contains("--light-theme", StringComparer.OrdinalIgnoreCase))
        {
            _host.Services.GetRequiredService<ThemeService>().UseLight();
        }

        _host.Services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            IHardwareMonitorProvider provider = _host.Services.GetRequiredService<IHardwareMonitorProvider>();
            provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _host.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            _host.Dispose();
        }

        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
