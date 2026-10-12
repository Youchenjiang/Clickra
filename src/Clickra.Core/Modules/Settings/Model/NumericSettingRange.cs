namespace Clickra.Core.Modules.Settings.Model;

/// <summary>Defines the valid bounds and default for one numeric setting.</summary>
public readonly record struct NumericSettingRange(int Min, int Max, int Default)
{
    public int Clamp(int value) => Math.Clamp(value, Min, Max);
    public bool IsInRange(int value) => value >= Min && value <= Max;
}
