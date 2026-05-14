using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinMcp.Platform.Config;

/// <summary>
/// Load and save the platform's <c>config.json</c>. Save is atomic — write
/// to <c>config.json.tmp</c>, then <c>File.Move(..., overwrite: true)</c>.
/// Carries forward the v1.0.20 atomic-write fix from math-mcp.
/// </summary>
public static class ConfigLoader
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static PlatformConfig Load(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            throw new ConfigException($"Failed to read {path}: {ex.Message}", ex);
        }

        PlatformConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<PlatformConfig>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"Invalid JSON in {path}: {ex.Message}", ex);
        }

        if (config is null)
        {
            throw new ConfigException($"Config in {path} parsed to null.");
        }

        ValidatePorts(config);
        return config;
    }

    public static void Save(PlatformConfig config, string path)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    public static PlatformConfig DefaultsForFreshInstall() => new();

    private static void ValidatePorts(PlatformConfig c)
    {
        static void RequireRange(int port, string field)
        {
            if (port < 1 || port > 65535)
            {
                throw new ConfigException($"{field} ({port}) must be in 1..65535");
            }
        }
        RequireRange(c.HttpPort, "httpPort");
        RequireRange(c.HttpsPort, "httpsPort");
        if (c.HttpPort == c.HttpsPort)
        {
            throw new ConfigException($"httpPort and httpsPort must differ; both are {c.HttpPort}");
        }
    }
}

public sealed class ConfigException : Exception
{
    public ConfigException(string message) : base(message) { }
    public ConfigException(string message, Exception inner) : base(message, inner) { }
}
