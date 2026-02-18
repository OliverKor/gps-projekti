namespace Gps.Ui.Wpf.Mqtt;

internal sealed record MqttSettingsLoadResult(MqttSettings Settings, string? ErrorMessage)
{
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
}
