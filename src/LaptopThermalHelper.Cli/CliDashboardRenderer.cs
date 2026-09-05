using System.Globalization;
using System.Text;
using System.Text.Json;
using LaptopThermalHelper.Application.Monitoring;
using LaptopThermalHelper.Core.Domain;

namespace LaptopThermalHelper.Cli;

public static class CliDashboardRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Render(MonitoringSnapshot snapshot, string osInfo, string mode)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var sb = new StringBuilder();
        sb.AppendLine("\u001b[1;36m========================================================================\u001b[0m");
        sb.AppendLine(CultureInfo.InvariantCulture, $"\u001b[1;37m 💻 笔记本温控助手 (Laptop Thermal Helper)\u001b[0m  \u001b[33m[{mode}]\u001b[0m");
        sb.AppendLine(CultureInfo.InvariantCulture, $"\u001b[90m 系统环境: {osInfo} | 时间: {snapshot.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss}\u001b[0m");
        sb.AppendLine("\u001b[1;36m========================================================================\u001b[0m");
        sb.AppendLine();

        if (snapshot.Devices.Count == 0)
        {
            sb.AppendLine(" \u001b[33m(未检测到可用传感器设备或正在等待初始采样...)\u001b[0m");
            sb.AppendLine();
            return sb.ToString();
        }

        sb.AppendLine(" \u001b[1m设备名称             类型   当前温度   最低/最高   负载/使用率   风扇转速   状态\u001b[0m");
        sb.AppendLine(" -----------------------------------------------------------------------");

        foreach (MonitoredDeviceSnapshot monitored in snapshot.Devices)
        {
            DeviceSnapshot device = monitored.Device;
            string kindText = device.Kind switch
            {
                DeviceKind.Cpu => "CPU",
                DeviceKind.Gpu => "GPU",
                DeviceKind.Storage => "SSD",
                DeviceKind.Memory => "RAM",
                _ => "SYS",
            };

            string tempText = device.Temperature.HasValue ? string.Create(CultureInfo.InvariantCulture, $"{device.Temperature.Value,5:F1} °C") : "    --  ";
            string minMaxText = (monitored.MinimumTemperature.HasValue && monitored.MaximumTemperature.HasValue)
                ? string.Create(CultureInfo.InvariantCulture, $"{monitored.MinimumTemperature.Value,4:F0}/{monitored.MaximumTemperature.Value,3:F0} °C")
                : "    --/ --  ";
            string loadText = device.Load.HasValue ? string.Create(CultureInfo.InvariantCulture, $"{device.Load.Value,5:F1} %") : "    --  ";
            string fanText = device.FanRpm.HasValue ? string.Create(CultureInfo.InvariantCulture, $"{device.FanRpm.Value,5:F0} RPM") : "   --    ";

            string statusText = FormatStatus(device.ThermalLevel);
            string truncatedName = TruncatePad(device.DisplayName, 20);

            sb.AppendLine(CultureInfo.InvariantCulture, $" {truncatedName} {kindText,4}   {tempText}   {minMaxText}    {loadText}     {fanText}  {statusText}");
        }

        sb.AppendLine();
        sb.AppendLine("\u001b[90m 按 Q 退出 | 自动刷新间隔: 1 秒\u001b[0m");
        return sb.ToString();
    }

    public static string RenderStatusLine(MonitoringSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var sb = new StringBuilder();
        foreach (MonitoredDeviceSnapshot monitored in snapshot.Devices)
        {
            DeviceSnapshot d = monitored.Device;
            string temp = d.Temperature.HasValue ? string.Create(CultureInfo.InvariantCulture, $"{d.Temperature.Value:F0}°C") : "--";
            string load = d.Load.HasValue ? string.Create(CultureInfo.InvariantCulture, $"{d.Load.Value:F0}%") : "--";
            sb.Append(CultureInfo.InvariantCulture, $"{d.Kind}: {temp} ({load}) | ");
        }

        if (sb.Length >= 3)
        {
            sb.Length -= 3;
        }

        return sb.ToString();
    }

    public static string RenderJson(MonitoringSnapshot snapshot, string osInfo, string mode)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var dto = new
        {
            Timestamp = snapshot.Timestamp,
            OperatingSystem = osInfo,
            Mode = mode,
            OverallThermalLevel = snapshot.SystemLevel.ToString(),
            Devices = snapshot.Devices.Select(d => new
            {
                d.Device.DeviceId,
                Kind = d.Device.Kind.ToString(),
                d.Device.DisplayName,
                TemperatureCelsius = d.Device.Temperature,
                MinimumCelsius = d.MinimumTemperature,
                MaximumCelsius = d.MaximumTemperature,
                AverageCelsius = d.AverageTemperature,
                LoadPercent = d.Device.Load,
                PowerWatts = d.Device.Power,
                FanRpm = d.Device.FanRpm,
                ThermalLevel = d.Device.ThermalLevel.ToString(),
            }),
        };

        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    private static string FormatStatus(ThermalLevel level) => level switch
    {
        ThermalLevel.Normal => "\u001b[32m● 正常\u001b[0m",
        ThermalLevel.Elevated => "\u001b[33m▲ 偏高\u001b[0m",
        ThermalLevel.High => "\u001b[31m▲ 过高\u001b[0m",
        ThermalLevel.Critical => "\u001b[1;31m✖ 严重\u001b[0m",
        _ => "\u001b[90m- 未知\u001b[0m",
    };

    private static string TruncatePad(string text, int length)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new string(' ', length);
        }

        if (text.Length > length)
        {
            return text[..(length - 2)] + "..";
        }

        return text.PadRight(length);
    }
}
