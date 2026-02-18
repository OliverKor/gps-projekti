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

    public static MqttSettingsLoadResult LoadFromAppBaseDirectory()
    {
        var path = Path.Combine(AppContext.BaseDirectory, SettingsFileName);
        if (!File.Exists(path))
        {
            return new MqttSettingsLoadResult(
                MqttSettings.DisabledDefaults,
                $"MQTT config missing: '{path}'.");
        }

        try
        {
            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<MqttSettings>(json, JsonOptions);
            if (parsed is null)
            {
                return new MqttSettingsLoadResult(
                    MqttSettings.DisabledDefaults,
                    $"MQTT config is invalid: '{path}'.");
            }

            return new MqttSettingsLoadResult(parsed.Sanitize(), null);
        }
        catch (Exception ex)
        {
            return new MqttSettingsLoadResult(
                MqttSettings.DisabledDefaults,
                $"MQTT config error: {ex.Message}");
        }
    }
}
