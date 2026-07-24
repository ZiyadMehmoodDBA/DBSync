using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MSOSync.Cli.Http;

public sealed class MsoSyncHttpClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly bool _owned;

    /// <summary>Production constructor — builds and owns an HttpClient.</summary>
    public MsoSyncHttpClient(string baseUrl, string? bearerToken = null)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout     = TimeSpan.FromSeconds(30)
        };
        if (!string.IsNullOrEmpty(bearerToken))
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", bearerToken);
        _owned = true;
    }

    /// <summary>Test constructor — accepts pre-built HttpClient (not disposed on Dispose).</summary>
    public MsoSyncHttpClient(HttpClient httpClient)
    {
        _http  = httpClient;
        _owned = false;
    }

    /// <summary>GET {path} and deserialize response as T. Returns null on empty body.</summary>
    public async Task<T?> GetAsync<T>(string path, CancellationToken ct = default)
    {
        HttpResponseMessage response = await _http.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body)) return default;
        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    /// <summary>GET {path} and return raw HttpResponseMessage (for status-code inspection).</summary>
    public Task<HttpResponseMessage> GetRawAsync(string path, CancellationToken ct = default)
        => _http.GetAsync(path, ct);

    /// <summary>POST {path} with JSON body — returns HttpResponseMessage for status-code inspection.</summary>
    public async Task<HttpResponseMessage> PostJsonAsync<T>(string path, T body, CancellationToken ct = default)
    {
        string json    = JsonSerializer.Serialize(body, JsonOptions);
        var    content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _http.PostAsync(path, content, ct);
    }

    /// <summary>POST {path} as multipart/form-data file upload — returns HttpResponseMessage.</summary>
    public async Task<HttpResponseMessage> PostMultipartAsync(
        string path, string fieldName, string filePath, CancellationToken ct = default)
    {
        await using FileStream fs      = File.OpenRead(filePath);
        using var             form     = new MultipartFormDataContent();
        using var             fileContent = new StreamContent(fs);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, fieldName, Path.GetFileName(filePath));
        return await _http.PostAsync(path, form, ct);
    }

    /// <summary>GET {path} with ApiKey header (registry auth) — returns HttpResponseMessage.</summary>
    public async Task<HttpResponseMessage> GetWithApiKeyAsync(
        string path, string apiKey, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Authorization", $"ApiKey {apiKey}");
        return await _http.SendAsync(request, ct);
    }

    public void Dispose()
    {
        if (_owned) _http.Dispose();
    }
}
