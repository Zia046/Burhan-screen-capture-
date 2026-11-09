namespace BurhanScreenCapture.Licensing;

/// <summary>Commercial license plans. Free is the absence of a license.</summary>
public enum LicensePlan
{
    Free = 0,
    Trial = 1,
    Monthly = 2,
    Yearly = 3,
    Lifetime = 4
}

public enum Edition
{
    Free,
    ProTrial,
    Pro
}

public static class LicensePlanInfo
{
    public const int TrialDays = 7;

    public static string DisplayName(LicensePlan plan) => plan switch
    {
        LicensePlan.Free => "Free Edition",
        LicensePlan.Trial => "Pro Trial (7 days)",
        LicensePlan.Monthly => "Pro Monthly",
        LicensePlan.Yearly => "Pro Yearly",
        LicensePlan.Lifetime => "Pro Lifetime",
        _ => plan.ToString()
    };

    /// <summary>Default validity used by the license generator when issuing a plan.</summary>
    public static TimeSpan? DefaultDuration(LicensePlan plan) => plan switch
    {
        LicensePlan.Trial => TimeSpan.FromDays(TrialDays),
        LicensePlan.Monthly => TimeSpan.FromDays(31),
        LicensePlan.Yearly => TimeSpan.FromDays(366),
        LicensePlan.Lifetime => null,
        _ => TimeSpan.Zero
    };
}
