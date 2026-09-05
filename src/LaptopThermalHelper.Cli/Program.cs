using System.Runtime.InteropServices;
using LaptopThermalHelper.Application.Hardware;
using LaptopThermalHelper.Application.Monitoring;
using LaptopThermalHelper.Hardware.Linux;
using LaptopThermalHelper.Hardware.Mac;

#if WINDOWS
using LaptopThermalHelper.Hardware.Lhm;
#endif

namespace LaptopThermalHelper.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        bool mock = args.Contains("--mock", StringComparer.OrdinalIgnoreCase);
        bool json = args.Contains("--json", StringComparer.OrdinalIgnoreCase);
        bool statusOnly = args.Contains("--status", StringComparer.OrdinalIgnoreCase);
        bool daemon = args.Contains("--daemon", StringComparer.OrdinalIgnoreCase);
        bool help = args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h", StringComparer.OrdinalIgnoreCase);

        if (help)
        {
            PrintHelp();
            return 0;
        }

        string osInfo = RuntimeInformation.OSDescription;
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        IHardwareMonitorProvider provider = CreateProvider(mock);
        await using (provider.ConfigureAwait(false))
        {
            using var coordinator = new MonitoringCoordinator(provider);
            string mode = mock ? "模拟模式 (Mock)" : "真实硬件 (Real Hardware)";

            // One-shot modes
            if (json || statusOnly)
            {
                MonitoringSnapshot snapshot = await coordinator.PollAsync(cts.Token).ConfigureAwait(false);
                if (json)
                {
                    Console.WriteLine(CliDashboardRenderer.RenderJson(snapshot, osInfo, mode));
                }
                else
                {
                    Console.WriteLine(CliDashboardRenderer.RenderStatusLine(snapshot));
                }
                return 0;
            }

            // Headless Daemon mode
            if (daemon)
            {
                Console.WriteLine($"[Daemon] 笔记本温控助手后台守护模式已启动 ({osInfo}, {mode})。按 Ctrl+C 停止。");
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        await coordinator.PollAsync(cts.Token).ConfigureAwait(false);
                        await Task.Delay(2000, cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
                Console.WriteLine("[Daemon] 后台守护进程已安全退出。");
                return 0;
            }

            // Interactive Dashboard mode
            bool isTerminal = !Console.IsOutputRedirected;
            if (isTerminal)
            {
                try
                {
                    Console.CursorVisible = false;
                }
                catch
                {
                    // Ignore if console handle does not support cursor visibility
                }
            }

            try
            {
                while (!cts.IsCancellationRequested)
                {
                    MonitoringSnapshot snapshot = await coordinator.PollAsync(cts.Token).ConfigureAwait(false);
                    string output = CliDashboardRenderer.Render(snapshot, osInfo, mode);

                    if (isTerminal)
                    {
                        Console.SetCursorPosition(0, 0);
                        Console.Write(output);
                    }
                    else
                    {
                        Console.WriteLine(output);
                    }

                    // Check for key press 'q' to exit
                    if (isTerminal && Console.KeyAvailable)
                    {
                        ConsoleKeyInfo key = Console.ReadKey(true);
                        if (key.Key == ConsoleKey.Q)
                        {
                            break;
                        }
                    }

                    try
                    {
                        await Task.Delay(1000, cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            finally
            {
                if (isTerminal)
                {
                    try
                    {
                        Console.CursorVisible = true;
                    }
                    catch
                    {
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("程序已退出。");
            return 0;
        }
    }

    private static IHardwareMonitorProvider CreateProvider(bool forceMock)
    {
        if (forceMock)
        {
            return new FakeHardwareMonitorProvider();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxHardwareMonitorProvider();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacHardwareMonitorProvider();
        }

#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            try
            {
                return new LhmHardwareMonitorProvider();
            }
            catch
            {
                return new FakeHardwareMonitorProvider();
            }
        }
#endif

        return new FakeHardwareMonitorProvider();
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"笔记本温控助手 (Laptop Thermal Helper) - 跨平台命令行工具

用法:
  LaptopThermalHelper.Cli [选项]

选项:
  --status        单次输出当前简要监控状态并退出 (适合脚本集成)
  --json          单次输出格式化 JSON 数据并退出 (适合 Waybar / Polybar / i3blocks)
  --daemon        以静默无头后台守护模式运行 (适合 systemd / launchd 服务)
  --mock          使用确定性的模拟传感器数据 (无需真实硬件)
  -h, --help      显示本帮助信息

交互快捷键:
  Q               退出控制台仪表盘
");
    }
}
