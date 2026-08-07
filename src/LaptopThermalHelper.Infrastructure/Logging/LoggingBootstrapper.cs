using System.Globalization;
using Serilog;

namespace LaptopThermalHelper.Infrastructure.Logging;

public static class LoggingBootstrapper
{
    public static ILogger CreateLogger()
    {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string logPath = Path.Combine(
            localData,
            "RoamerFly",
            "LaptopThermalHelper",
            "logs",
            "app-.log");

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                formatProvider: CultureInfo.InvariantCulture,
                shared: true)
            .CreateLogger();
    }
}
