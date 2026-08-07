namespace LaptopThermalHelper.Core.Domain;

/// <summary>
/// Describes whether the operating-system metadata could be read. Individual
/// fields can still be unavailable when the overall result is partial.
/// </summary>
public enum SystemInformationAvailability
{
    Ready,
    Partial,
    Unavailable,
}

public enum BatteryChargeState
{
    Unknown,
    Charging,
    Discharging,
    Full,
    NotPresent,
}

public enum PowerSourceKind
{
    Unknown,
    Ac,
    Battery,
}

public sealed record BatteryInformation(
    BatteryChargeState State,
    int? ChargePercent,
    TimeSpan? RemainingTime)
{
    public bool IsPresent => State != BatteryChargeState.NotPresent;
}

public sealed record PowerPlanInformation(
    Guid? Identifier,
    string DisplayName);

/// <summary>
/// Read-only machine information for binding in the shell, status bar and
/// about page. Missing information is represented by <c>"不可用"</c> rather
/// than substituted example hardware values.
/// </summary>
public sealed record SystemInformationSnapshot(
    string OperatingSystem,
    string Manufacturer,
    string Model,
    BatteryInformation Battery,
    PowerSourceKind PowerSource,
    PowerPlanInformation PowerPlan,
    SystemInformationAvailability Availability,
    string? Diagnostic,
    DateTimeOffset RetrievedAt)
{
    public const string UnavailableText = "不可用";

    public static SystemInformationSnapshot Unavailable(
        DateTimeOffset retrievedAt,
        string? diagnostic = null) =>
        new(
            UnavailableText,
            UnavailableText,
            UnavailableText,
            new BatteryInformation(BatteryChargeState.Unknown, null, null),
            PowerSourceKind.Unknown,
            new PowerPlanInformation(null, UnavailableText),
            SystemInformationAvailability.Unavailable,
            diagnostic,
            retrievedAt);
}
