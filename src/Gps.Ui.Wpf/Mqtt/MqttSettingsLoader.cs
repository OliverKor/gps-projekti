using System.Text.Json;
using System.IO;

namespace Gps.Ui.Wpf.Mqtt;

internal static class MqttSettingsLoader
{
    private const string SettingsFileName = "mqttsettings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static MqttSettings LoadFromAppBaseDirectory()
    {
        var path = Path.Combine(AppContext.BaseDirectory, SettingsFileName);
        if (!File.Exists(path))
        {
            return MqttSettings.DisabledDefaults;
        }

        try
        {
            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<MqttSettings>(json, JsonOptions);
            return (parsed ?? MqttSettings.DisabledDefaults).Sanitize();
        }
        catch
        {
            return MqttSettings.DisabledDefaults;
        }
    }
}
