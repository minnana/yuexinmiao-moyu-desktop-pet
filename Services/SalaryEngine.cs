using YueXinCat.Models;

namespace YueXinCat.Services;

/// <summary>
/// Pure schedule and money calculations. It does not own a timer or touch the UI.
/// Callers can therefore use it for both the one-second display tick and startup
/// catch-up after the process was closed.
/// </summary>
public sealed class SalaryEngine
{
    private readonly SalarySettings _settings;

    public SalaryEngine(SalarySettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settings.Validate();
    }

    public SalarySettings Settings => _settings;

    public int GetCalendarDays(int year, int month) => DateTime.DaysInMonth(year, month);

    public int GetWorkdays(int year, int month)
    {
        var days = GetCalendarDays(year, month);
        return Math.Max(0, days - Math.Clamp(_settings.MonthlyRestDays, 0, days));
    }

    public TimeSpan GetShiftDuration()
    {
        var duration = _settings.WorkEnd - _settings.WorkStart;
        if (duration <= TimeSpan.Zero)
            duration += TimeSpan.FromDays(1);
        return duration;
    }

    public TimeSpan GetDailyPaidDuration()
    {
        var shiftStart = _settings.WorkStart;
        var shiftEnd = _settings.WorkEnd;
        if (shiftEnd <= shiftStart)
            shiftEnd += TimeSpan.FromDays(1);

        var lunchStart = _settings.LunchStart;
        if (lunchStart < shiftStart)
            lunchStart += TimeSpan.FromDays(1);
        var lunchEnd = lunchStart + _settings.LunchBreak;
        var overlapStart = lunchStart > shiftStart ? lunchStart : shiftStart;
        var overlapEnd = lunchEnd < shiftEnd ? lunchEnd : shiftEnd;
        var unpaid = overlapEnd > overlapStart ? overlapEnd - overlapStart : TimeSpan.Zero;
        return MaxZero(shiftEnd - shiftStart - unpaid);
    }

    /// <summary>Base second rate for the month containing <paramref name="date"/>.</summary>
    public decimal GetSecondPay(DateOnly date)
    {
        var seconds = GetWorkdays(date.Year, date.Month) * GetDailyPaidDuration().TotalSeconds;
        return seconds <= 0 ? 0m : _settings.MonthlyGrossSalary / (decimal)seconds;
    }

    public bool IsRestDay(DateOnly date) =>
        _settings.RestDates.Contains(date) || _settings.RestDayNumbers.Contains(date.Day);

    public bool IsPaidTime(DateTimeOffset at, out bool isOvertime)
    {
        isOvertime = false;
        var intervals = BuildPayIntervals(DateOnly.FromDateTime(at.DateTime), at.Offset);
        // Include yesterday so a cross-midnight shift remains payable after midnight.
        intervals.AddRange(BuildPayIntervals(DateOnly.FromDateTime(at.DateTime.Date.AddDays(-1)), at.Offset));
        foreach (var interval in intervals)
        {
            if (at >= interval.Start && at < interval.End)
            {
                isOvertime = interval.Multiplier > 1m;
                return interval.Multiplier > 0m;
            }
        }
        return false;
    }

    public SalarySnapshot GetSnapshot(SalaryState state, DateTimeOffset at, string? currentKey = null)
    {
        var increment = CalculateIncrement(state.BalanceAsOf, at);
        var paid = IsPaidTime(at, out var overtime);
        var rate = GetSecondPay(DateOnly.FromDateTime(at.DateTime));
        var status = paid ? (overtime ? "加班中" : "工作中") : (IsRestDay(DateOnly.FromDateTime(at.DateTime)) ? "休息日" : "休息中");
        return new SalarySnapshot(at, state.Balance + increment, increment, rate, paid, overtime, status, currentKey);
    }

    /// <summary>
    /// Calculates every payable slice between two timestamps. Fractional seconds
    /// are retained; the caller decides how often to redraw the visible amount.
    /// </summary>
    public decimal CalculateIncrement(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from)
            return 0m;

        var total = 0m;
        var cursor = DateOnly.FromDateTime(from.DateTime.Date.AddDays(-1));
        var endDate = DateOnly.FromDateTime(to.DateTime.Date);
        while (cursor <= endDate)
        {
            var offset = from.Offset;
            foreach (var interval in BuildPayIntervals(cursor, offset))
            {
                var start = interval.Start > from ? interval.Start : from;
                var end = interval.End < to ? interval.End : to;
                if (end > start)
                {
                    var seconds = (decimal)(end - start).TotalSeconds;
                    var rate = GetSecondPay(DateOnly.FromDateTime(start.DateTime));
                    total += seconds * rate * interval.Multiplier;
                }
            }
            cursor = cursor.AddDays(1);
        }
        return total;
    }

    private List<PayInterval> BuildPayIntervals(DateOnly date, TimeSpan offset)
    {
        var intervals = new List<PayInterval>();
        if (!IsRestDay(date))
        {
            var shiftStart = At(date, _settings.WorkStart, offset);
            var shiftEnd = At(date, _settings.WorkEnd, offset);
            if (shiftEnd <= shiftStart)
                shiftEnd = shiftEnd.AddDays(1);

            var lunchStart = At(date, _settings.LunchStart, offset);
            if (lunchStart < shiftStart)
                lunchStart = lunchStart.AddDays(1);
            var lunchEnd = lunchStart + _settings.LunchBreak;
            AddWithoutLunch(intervals, shiftStart, shiftEnd, lunchStart, lunchEnd, 1m);
        }

        foreach (var overtime in _settings.OvertimeWindows.Where(item => item.Date == date))
        {
            var start = At(date, overtime.Start, offset);
            var end = At(date, overtime.End, offset);
            if (end <= start)
                end = end.AddDays(1);
            if (end > start && overtime.Multiplier > 0m)
                intervals.Add(new PayInterval(start, end, overtime.Multiplier));
        }

        return MergeOverlaps(intervals);
    }

    private static void AddWithoutLunch(List<PayInterval> target, DateTimeOffset start, DateTimeOffset end,
        DateTimeOffset lunchStart, DateTimeOffset lunchEnd, decimal multiplier)
    {
        if (end <= start)
            return;
        if (lunchEnd <= start || lunchStart >= end || lunchStart >= lunchEnd)
        {
            target.Add(new PayInterval(start, end, multiplier));
            return;
        }
        if (lunchStart > start)
            target.Add(new PayInterval(start, lunchStart < end ? lunchStart : end, multiplier));
        if (lunchEnd < end)
            target.Add(new PayInterval(lunchEnd > start ? lunchEnd : start, end, multiplier));
    }

    /// <summary>
    /// Overlapping base/overtime slices are represented by the highest multiplier,
    /// preventing an overtime interval from paying the same second twice.
    /// </summary>
    private static List<PayInterval> MergeOverlaps(List<PayInterval> source)
    {
        if (source.Count < 2)
            return source;
        var points = source.SelectMany(item => new[] { item.Start, item.End }).Distinct().OrderBy(point => point).ToArray();
        var result = new List<PayInterval>();
        for (var i = 0; i + 1 < points.Length; i++)
        {
            var start = points[i];
            var end = points[i + 1];
            var multiplier = source.Where(item => item.Start < end && item.End > start)
                .Select(item => item.Multiplier).DefaultIfEmpty(0m).Max();
            if (multiplier <= 0m)
                continue;
            if (result.Count > 0 && result[^1].End == start && result[^1].Multiplier == multiplier)
                result[^1] = result[^1] with { End = end };
            else
                result.Add(new PayInterval(start, end, multiplier));
        }
        return result;
    }

    private static DateTimeOffset At(DateOnly date, TimeSpan time, TimeSpan offset) =>
        new(date.ToDateTime(TimeOnly.FromTimeSpan(time)), offset);

    private static TimeSpan MaxZero(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;

    private sealed record PayInterval(DateTimeOffset Start, DateTimeOffset End, decimal Multiplier);
}
