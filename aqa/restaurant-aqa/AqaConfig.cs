namespace RestaurantAqa;

/// <summary>
/// Test configuration is private. GitHub Actions overrides .env with repository variables/secrets.
/// Never print this configuration in test logs.
/// </summary>
public sealed class AqaConfig
{
    private static readonly Lazy<Dictionary<string, string>> Local = new(ReadDotEnv);

    private static string? Value(string key)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(key);
        return !string.IsNullOrWhiteSpace(fromEnvironment)
            ? fromEnvironment
            : Local.Value.GetValueOrDefault(key);
    }

    private static string Required(string key) =>
        Value(key) ?? throw new InvalidOperationException($"Missing AQA configuration: {key}. See .env.example and README.");

    public string ApiUrl => Required("AQA_BASE_URL").TrimEnd('/') + "/";
    public string UiUrl => Required("AQA_UI_URL").TrimEnd('/');
    public string OwnerEmail => Required("AQA_OWNER_EMAIL");
    public string OwnerPassword => Required("AQA_OWNER_PASSWORD");
    public string OtherEmail => Required("AQA_OTHER_EMAIL");
    public string OtherPassword => Required("AQA_OTHER_PASSWORD");
    public string WaiterEmail => Required("AQA_WAITER_EMAIL");
    public string WaiterPassword => Required("AQA_WAITER_PASSWORD");
    public string TableId => Required("AQA_TABLE_ID");
    public string DishId => Required("AQA_DISH_ID");

    private static Dictionary<string, string> ReadDotEnv()
    {
        // Work in VS Code, from dotnet test, and after compilation from bin/Debug/net10.0.
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            // `dotnet test` is normally started in the repository root.
            // Also support the project-local configuration at aqa/restaurant-aqa/.env.
            var localToProject = Path.Combine(directory.FullName, "aqa", "restaurant-aqa", ".env");
            var path = File.Exists(localToProject)
                ? localToProject
                : Path.Combine(directory.FullName, ".env");
            if (File.Exists(path))
            {
                var entries = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var line in File.ReadLines(path))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                    var index = trimmed.IndexOf('=');
                    if (index <= 0) continue;
                    entries[trimmed[..index].Trim()] = trimmed[(index + 1)..].Trim().Trim('"');
                }
                return entries;
            }
            directory = directory.Parent;
        }
        return new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
