namespace WebhookPlugin;

internal sealed record WebhookPayload(
    string PluginId,
    string Event,
    string Message,
    DateTime Timestamp);
