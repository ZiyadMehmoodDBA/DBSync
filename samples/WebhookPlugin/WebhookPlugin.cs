using System.Text;
using System.Text.Json;
using MSOSync.Sdk.Abstractions;
using MSOSync.Sdk.Hosting;

namespace WebhookPlugin;

public sealed class WebhookPlugin : PluginBase
{
    private HttpClient? _httpClient;
    private bool _ownsHttpClient;

    public override async Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        await base.InitializeAsync(context, cancellationToken);

        // Validate webhook URL at init time
        var webhookUrl = Context.Configuration.GetValue<string>("WebhookUrl", "");
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            Context.Logger.LogWarning("No WebhookUrl configured; webhook delivery disabled");
        }
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = Context.Logger.BeginScope("WebhookPlugin.Start");

        // Try to get HttpClientFactory from host services
        var factory = Context.Services.GetService<IHttpClientFactory>();

        if (factory != null)
        {
            _httpClient = factory.CreateClient();
            _ownsHttpClient = false;
            Context.Logger.LogInformation("Using host-provided IHttpClientFactory");
        }
        else
        {
            _httpClient = new HttpClient();
            _ownsHttpClient = true;
            Context.Logger.LogInformation("No IHttpClientFactory available; using standalone HttpClient");
        }

        // Post startup notification
        _ = PostWebhookAsync("plugin.started", "Plugin started");

        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        using var scope = Context.Logger.BeginScope("WebhookPlugin.Stop");
        Context.Logger.LogInformation("WebhookPlugin stopping");
        return Task.CompletedTask;
    }

    public override async ValueTask DisposeAsync()
    {
        if (_ownsHttpClient && _httpClient != null)
        {
            _httpClient.Dispose();
        }

        await base.DisposeAsync();
    }

    private async Task PostWebhookAsync(string eventName, string message)
    {
        try
        {
            var webhookUrl = Context.Configuration.GetValue<string>("WebhookUrl", "");
            if (string.IsNullOrWhiteSpace(webhookUrl) || _httpClient == null)
            {
                return;
            }

            var timeout = Context.Configuration
                .GetValue("TimeoutSeconds", 10);
            var retryCount = Context.Configuration
                .GetValue("RetryCount", 3);

            var payload = new WebhookPayload(
                Context.Metadata.PluginId,
                eventName,
                message,
                DateTime.UtcNow);

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));

            Exception? lastException = null;
            for (int attempt = 1; attempt <= retryCount; attempt++)
            {
                try
                {
                    var response = await _httpClient.PostAsync(webhookUrl, content, cts.Token);

                    if (response.IsSuccessStatusCode)
                    {
                        Context.Logger.LogDebug(
                            "Webhook delivered: {Event} to {Url}",
                            eventName,
                            webhookUrl);
                        return;
                    }

                    Context.Logger.LogWarning(
                        "Webhook delivery failed: {Event} to {Url} returned {StatusCode}",
                        eventName,
                        webhookUrl,
                        response.StatusCode);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (attempt < retryCount)
                    {
                        Context.Logger.LogDebug(
                            "Webhook delivery attempt {Attempt}/{Total} failed, retrying",
                            attempt,
                            retryCount);
                        await Task.Delay(TimeSpan.FromSeconds(1));
                    }
                }
            }

            if (lastException != null)
            {
                Context.Logger.LogError(lastException,
                    "Webhook delivery exhausted retries: {Event} to {Url}",
                    eventName,
                    webhookUrl);
            }
        }
        catch (Exception ex)
        {
            Context.Logger.LogError(ex, "Unexpected error in webhook delivery");
        }
    }
}
