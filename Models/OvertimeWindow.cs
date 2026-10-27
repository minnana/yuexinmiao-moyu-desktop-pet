namespace YueXinCat.Models;

/// <summary>
/// An overtime interval belonging to the date on which the interval starts.
/// End earlier than or equal to start means that the interval crosses midnight.
/// </summary>
public sealed class OvertimeWindow
{
    public DateOnly Date { get; set; }
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }

    /// <summary>Multiplier applied to the normal second rate during this interval.</summary>
    public decimal Multiplier { get; set; } = 1.5m;

    public OvertimeWindow Clone() => new()
    {
        Date = Date,
        Start = Start,
        End = End,
        Multiplier = Multiplier
    };
}
