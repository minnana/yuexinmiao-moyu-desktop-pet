namespace YueXinCat.Models;

/// <summary>
/// User-editable salary and schedule settings. Monetary values are decimal so
/// that a negative starting balance remains exact when saved and restored.
/// </summary>
public sealed class SalarySettings
{
    public decimal MonthlyGrossSalary { get; set; } = 10_000m;
    public decimal InitialBalance { get; set; }
    public int MonthlyRestDays { get; set; } = 8;
    public TimeSpan WorkStart { get; set; } = new(9, 0, 0);
    public TimeSpan WorkEnd { get; set; } = new(18, 0, 0);

    /// <summary>Unpaid lunch duration. The lunch starts at LunchStart.</summary>
    public TimeSpan LunchBreak { get; set; } = new(1, 0, 0);
    public TimeSpan LunchStart { get; set; } = new(12, 0, 0);

    /// <summary>
    /// Optional explicit rest dates. MonthlyRestDays remains the source of the
    /// month denominator; these dates tell the live clock which dates are off.
    /// </summary>
    public HashSet<DateOnly> RestDates { get; set; } = [];

    /// <summary>
    /// Optional day numbers used when a repeating monthly schedule is preferred.
    /// Values outside the current month are ignored.
    /// </summary>
    public HashSet<int> RestDayNumbers { get; set; } = [];

    public List<OvertimeWindow> OvertimeWindows { get; set; } = [];
    public bool KeyboardTrackingEnabled { get; set; }
    public bool StartWithWindows { get; set; }
    /// <summary>Desk pet size as a percentage of the compact default.</summary>
    public int PetSizePercent { get; set; } = 65;

    public SalarySettings Clone() => new()
    {
        MonthlyGrossSalary = MonthlyGrossSalary,
        InitialBalance = InitialBalance,
        MonthlyRestDays = MonthlyRestDays,
        WorkStart = WorkStart,
        WorkEnd = WorkEnd,
        LunchBreak = LunchBreak,
        LunchStart = LunchStart,
        RestDates = [.. RestDates],
        RestDayNumbers = [.. RestDayNumbers],
        OvertimeWindows = OvertimeWindows.Select(window => window.Clone()).ToList(),
        KeyboardTrackingEnabled = KeyboardTrackingEnabled,
        StartWithWindows = StartWithWindows,
        PetSizePercent = PetSizePercent
    };

    public void Validate()
    {
        if (MonthlyGrossSalary < 0)
            throw new ArgumentOutOfRangeException(nameof(MonthlyGrossSalary), "月薪不能为负数。");
        if (MonthlyRestDays < 0 || MonthlyRestDays > 31)
            throw new ArgumentOutOfRangeException(nameof(MonthlyRestDays), "月休天数应在 0 到 31 之间。");
        if (PetSizePercent < 40 || PetSizePercent > 150)
            throw new ArgumentOutOfRangeException(nameof(PetSizePercent), "桌宠大小应在 40% 到 150% 之间。");
        ValidateTime(WorkStart, nameof(WorkStart));
        ValidateTime(WorkEnd, nameof(WorkEnd));
        ValidateTime(LunchStart, nameof(LunchStart));
        if (LunchBreak < TimeSpan.Zero || LunchBreak > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(LunchBreak));
        foreach (var overtime in OvertimeWindows)
        {
            ValidateTime(overtime.Start, nameof(overtime.Start));
            ValidateTime(overtime.End, nameof(overtime.End));
            if (overtime.Multiplier < 0)
                throw new ArgumentOutOfRangeException(nameof(overtime.Multiplier));
        }
    }

    private static void ValidateTime(TimeSpan value, string name)
    {
        if (value < TimeSpan.Zero || value >= TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(name, "时间必须在 00:00 到 23:59:59 之间。");
    }
}
