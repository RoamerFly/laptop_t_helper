using System.Windows;
using LaptopThermalHelper.App.Services;
using LaptopThermalHelper.App.ViewModels;
using LaptopThermalHelper.Application.Hardware;
using LaptopThermalHelper.Application.History;
using LaptopThermalHelper.Application.Monitoring;
using LaptopThermalHelper.Application.System;
using LaptopThermalHelper.Hardware.Lhm;
using LaptopThermalHelper.Infrastructure.History;
using LaptopThermalHelper.Infrastructure.Logging;
using LaptopThermalHelper.Infrastructure.Platform;
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
        HardwareRuntimeOptions hardwareOptions = HardwareRuntimeOptions.Parse(e.Args);
        if (hardwareOptions.UsesDeprecatedRealHardwareFlag)
        {
            Log.Warning("--real-hardware 已废弃：真实硬件读取现在是默认模式；使用 --mock 可启用模拟数据。");
        }

        _host = Host.CreateDefaultBuilder(e.Args)
            .UseSerilog()
            .ConfigureServices(services =>
            {
                services.AddSingleton<ThemeService>();
                services.AddSingleton(hardwareOptions);
                services.AddSingleton<IHardwareMonitorProvider>(_ => hardwareOptions.UseMock
                    ? new FakeHardwareMonitorProvider()
                    : new LhmHardwareMonitorProvider());
                services.AddSingleton<ITemperatureHistoryStore, CsvTemperatureHistoryStore>();
                services.AddSingleton<ITemperatureHistoryBuffer, RollingTemperatureHistoryBuffer>();
                services.AddSingleton<ISystemInformationProvider, WindowsSystemInformationProvider>();
                services.AddSingleton<IIntelGpuDriverDetector, WindowsIntelGpuDriverDetector>();
                services.AddSingleton<IApplicationSettingsStore, JsonApplicationSettingsStore>();
                services.AddSingleton<IUserStartupRegistrationService, UserStartupRegistrationService>();
                services.AddSingleton<IApplicationEventLog, InMemoryApplicationEventLog>();
                services.AddSingleton<ITrayIconService, WindowsTrayIconService>();
                services.AddSingleton<IUserNotificationSink, TrayNotificationSink>();
                services.AddSingleton<ICriticalAlertSoundPlayer, SystemCriticalAlertSoundPlayer>();
                services.AddSingleton<ThermalNotificationService>();
                services.AddSingleton<IPowerPlanAdapter, PowerCfgPowerPlanAdapter>();
                services.AddSingleton<IAutoCoolingRecoveryStore, JsonAutoCoolingRecoveryStore>();
                services.AddSingleton<AutoCoolingService>();
                services.AddSingleton<SystemIntegrationService>();
                services.AddSingleton<IApplicationRuntimeInfo, ApplicationRuntimeInfo>();
                services.AddSingleton<MonitoringCoordinator>();
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<ShellViewModel>();
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
            try
            {
                using var shutdownCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                _host.Services.GetRequiredService<SystemIntegrationService>()
                    .ShutdownAsync(shutdownCancellation.Token)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception exception)
            {
                Log.Error(exception, "应用退出时恢复电源设置失败");
            }

            IHardwareMonitorProvider provider = _host.Services.GetRequiredService<IHardwareMonitorProvider>();
            provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _host.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            _host.Dispose();
        }

        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
