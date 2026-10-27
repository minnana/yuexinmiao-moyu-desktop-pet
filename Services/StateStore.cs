using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using YueXinCat.Models;

namespace YueXinCat.Services;

/// <summary>Atomic local JSON storage for settings, balance, and the last anchor.</summary>
public sealed class StateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public StateStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "YueXinCat",
            "state.json");
    }

    public string FilePath { get; }

    public AppState Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return CreateDefault();
            var json = File.ReadAllText(FilePath);
            var state = JsonSerializer.Deserialize<AppState>(json, JsonOptions);
            if (state?.Settings is null || state.Salary is null)
                return CreateDefault();
            return state;
        }
        catch (IOException)
        {
            return CreateDefault();
        }
        catch (JsonException)
        {
            return CreateDefault();
        }
    }

    public void Save(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Settings.Validate();
        state.Salary.SavedAt = DateTimeOffset.Now;
        var directory = Path.GetDirectoryName(FilePath) ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(directory);
        var temporary = FilePath + ".tmp";
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(temporary, json);
        File.Move(temporary, FilePath, true);
    }

    public void SaveBalance(AppState state, decimal balance, DateTimeOffset asOf)
    {
        state.Salary.Balance = balance;
        state.Salary.BalanceAsOf = asOf;
        Save(state);
    }

    private static AppState CreateDefault()
    {
        var now = DateTimeOffset.Now;
        var settings = new SalarySettings();
        return new AppState
        {
            Settings = settings,
            Salary = new SalaryState
            {
                Balance = settings.InitialBalance,
                BalanceAsOf = now,
                SavedAt = now
            }
        };
    }
}
