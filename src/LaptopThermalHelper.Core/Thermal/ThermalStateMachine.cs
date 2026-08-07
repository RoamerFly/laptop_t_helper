using LaptopThermalHelper.Core.Domain;

namespace LaptopThermalHelper.Core.Thermal;

public sealed class ThermalStateMachine
{
    private readonly TemperatureThresholds _thresholds;
    private ThermalLevel? _pendingLevel;
    private DateTimeOffset? _pendingSince;

    public ThermalStateMachine(TemperatureThresholds thresholds)
    {
        thresholds.Validate();
        _thresholds = thresholds;
    }

    public ThermalLevel CurrentLevel { get; private set; } = ThermalLevel.Unknown;

    public ThermalLevel Observe(double? temperature, DateTimeOffset timestamp)
    {
        if (!IsValid(temperature))
        {
            ResetPending();
            CurrentLevel = ThermalLevel.Unknown;
            return CurrentLevel;
        }

        double value = temperature!.Value;
        ThermalLevel measuredLevel = _thresholds.Classify(value);

        if (CurrentLevel == ThermalLevel.Unknown)
        {
            if (measuredLevel == ThermalLevel.Normal)
            {
                CurrentLevel = ThermalLevel.Normal;
                ResetPending();
                return CurrentLevel;
            }

            return TrackEscalation(measuredLevel, timestamp);
        }

        if (measuredLevel > CurrentLevel)
        {
            return TrackEscalation(measuredLevel, timestamp);
        }

        if (ShouldRecover(value))
        {
            return TrackRecovery(timestamp);
        }

        ResetPending();
        return CurrentLevel;
    }

    private ThermalLevel TrackEscalation(ThermalLevel measuredLevel, DateTimeOffset timestamp)
    {
        TrackPending(measuredLevel, timestamp);

        if (timestamp - _pendingSince!.Value >= _thresholds.EscalationDelay(measuredLevel))
        {
            CurrentLevel = measuredLevel;
            ResetPending();
        }

        return CurrentLevel;
    }

    private ThermalLevel TrackRecovery(DateTimeOffset timestamp)
    {
        ThermalLevel nextLevel = (ThermalLevel)((int)CurrentLevel - 1);
        TrackPending(nextLevel, timestamp);

        if (timestamp - _pendingSince!.Value >= _thresholds.RecoveryDelay)
        {
            CurrentLevel = nextLevel;
            ResetPending();
        }

        return CurrentLevel;
    }

    private bool ShouldRecover(double temperature) =>
        CurrentLevel > ThermalLevel.Normal &&
        temperature < _thresholds.LowerBound(CurrentLevel) - _thresholds.RecoveryHysteresis;

    private void TrackPending(ThermalLevel level, DateTimeOffset timestamp)
    {
        if (_pendingLevel == level)
        {
            return;
        }

        _pendingLevel = level;
        _pendingSince = timestamp;
    }

    private void ResetPending()
    {
        _pendingLevel = null;
        _pendingSince = null;
    }

    private static bool IsValid(double? temperature) =>
        temperature is >= -20 and <= 150 &&
        !double.IsNaN(temperature.Value) &&
        !double.IsInfinity(temperature.Value);
}
