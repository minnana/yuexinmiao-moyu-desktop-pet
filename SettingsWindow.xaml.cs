using System.Globalization;
using System.Text;
using System.Windows;
using YueXinCat.Models;
using YueXinCat.Services;

namespace YueXinCat;

public partial class SettingsWindow : Window
{
    private readonly SalarySettings _original;
    private readonly decimal _currentBalance;

    public SettingsWindow(SalarySettings settings, decimal currentBalance)
    {
        InitializeComponent();
        _original = settings.Clone();
        _currentBalance = currentBalance;
        LoadValues();
    }

    public SalarySettings? SavedSettings { get; private set; }
    public decimal SavedBalance { get; private set; }

    private void LoadValues()
    {
        BalanceBox.Text = Format(_currentBalance);
        SalaryBox.Text = Format(_original.MonthlyGrossSalary);
        RestDaysBox.Text = _original.MonthlyRestDays.ToString(CultureInfo.InvariantCulture);
        WorkStartBox.Text = FormatTime(_original.WorkStart);
        WorkEndBox.Text = FormatTime(_original.WorkEnd);
        LunchEnabledBox.IsChecked = _original.LunchBreak > TimeSpan.Zero;
        LunchStartBox.Text = FormatTime(_original.LunchStart);
        LunchHoursBox.Text = (_original.LunchBreak.TotalHours).ToString("0.##", CultureInfo.InvariantCulture);
        RestDatesBox.Text = string.Join(", ", _original.RestDayNumbers.OrderBy(x => x)
            .Cast<object>().Concat(_original.RestDates.OrderBy(x => x).Select(x => x.ToString("yyyy-MM-dd"))));
        OvertimeBox.Text = string.Join(Environment.NewLine, _original.OvertimeWindows.OrderBy(x => x.Date).ThenBy(x => x.Start)
            .Select(x => $"{x.Date:yyyy-MM-dd} {FormatTime(x.Start)}-{FormatTime(x.End)} {x.Multiplier.ToString("0.##", CultureInfo.InvariantCulture)}"));
        PetSizeBox.Text = _original.PetSizePercent.ToString(CultureInfo.InvariantCulture);
        KeyboardTrackingBox.IsChecked = _original.KeyboardTrackingEnabled;
        StartWithWindowsBox.IsChecked = _original.StartWithWindows || new StartupService().IsEnabled;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = _original.Clone();
            var balance = ParseDecimal(BalanceBox.Text, "当前余额");
            settings.MonthlyGrossSalary = ParseDecimal(SalaryBox.Text, "税前月薪");
            settings.MonthlyRestDays = ParseInt(RestDaysBox.Text, "每月休息天数");
            settings.WorkStart = ParseTime(WorkStartBox.Text, "上班开始");
            settings.WorkEnd = ParseTime(WorkEndBox.Text, "上班结束");
            settings.LunchStart = ParseTime(LunchStartBox.Text, "午休开始");
            settings.LunchBreak = LunchEnabledBox.IsChecked == true
                ? TimeSpan.FromHours((double)ParseDecimal(LunchHoursBox.Text, "午休时长"))
                : TimeSpan.Zero;
            ParseRestDays(RestDatesBox.Text, settings);
            settings.OvertimeWindows = ParseOvertime(OvertimeBox.Text);
            settings.PetSizePercent = ParseInt(PetSizeBox.Text, "桌宠大小");
            settings.KeyboardTrackingEnabled = KeyboardTrackingBox.IsChecked == true;
            settings.StartWithWindows = StartWithWindowsBox.IsChecked == true;
            settings.Validate();
            _ = new SalaryEngine(settings); // validate the complete schedule before closing the dialog

            SavedSettings = settings;
            SavedBalance = balance;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string FormatTime(TimeSpan value) => value.ToString(@"hh\:mm");

    private static decimal ParseDecimal(string text, string label)
    {
        if (!decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"{label}请输入数字。" );
        return value;
    }

    private static int ParseInt(string text, string label)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"{label}请输入整数。" );
        return value;
    }

    private static TimeSpan ParseTime(string text, string label)
    {
        if (TimeSpan.TryParse(text.Trim(), CultureInfo.InvariantCulture, out var value) && value >= TimeSpan.Zero && value < TimeSpan.FromDays(1))
            return value;
        throw new FormatException($"{label}请使用 09:00 这样的时间格式。" );
    }

    private static void ParseRestDays(string text, SalarySettings settings)
    {
        settings.RestDayNumbers.Clear();
        settings.RestDates.Clear();
        foreach (var raw in text.Split(new[] { ',', '，', ';', '；', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                settings.RestDates.Add(date);
                continue;
            }
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day) && day is >= 1 and <= 31)
            {
                settings.RestDayNumbers.Add(day);
                continue;
            }
            throw new FormatException($"休息日“{raw}”无法识别，请写日期或 1 到 31 的日序号。" );
        }
    }

    private static List<OvertimeWindow> ParseOvertime(string text)
    {
        var result = new List<OvertimeWindow>();
        var lineNumber = 0;
        foreach (var rawLine in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            lineNumber++;
            var parts = rawLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || !DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new FormatException($"加班第 {lineNumber} 行应为：2026-10-14 19:00-21:00 1.5。" );

            var range = parts[1].Replace('–', '-').Replace('—', '-');
            var dash = range.IndexOf('-');
            if (dash <= 0 || dash == range.Length - 1)
                throw new FormatException($"加班第 {lineNumber} 行的时间段无法识别。" );
            var start = ParseTime(range[..dash], "加班开始");
            var end = ParseTime(range[(dash + 1)..], "加班结束");
            var multiplier = parts.Length >= 3 ? ParseDecimal(parts[2].TrimStart('x', 'X', '倍'), "加班倍率") : 1.5m;
            if (multiplier < 0)
                throw new FormatException($"加班第 {lineNumber} 行的倍率不能为负数。" );
            result.Add(new OvertimeWindow { Date = date, Start = start, End = end, Multiplier = multiplier });
        }
        return result;
    }
}
