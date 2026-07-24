namespace MSOSync.CliTests.Helpers;

/// <summary>
/// Synchronous fake HttpMessageHandler for unit-testing MsoSyncHttpClient.
/// Pass a factory function that receives the outbound request and returns a canned response.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        => _handler = handler;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(_handler(request));
}
