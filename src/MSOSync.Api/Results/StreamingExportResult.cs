using Microsoft.AspNetCore.Mvc;

namespace MSOSync.Api.Results;

public sealed class StreamingExportResult : IActionResult
{
    private readonly Func<Stream, CancellationToken, Task<int>> _writer;
    private readonly string _contentType;
    private readonly string _fileName;
    private readonly Func<int, long, Task>? _onComplete;
    private readonly CancellationToken _ct;

    public StreamingExportResult(
        Func<Stream, CancellationToken, Task<int>> writer,
        string contentType,
        string fileName,
        Func<int, long, Task>? onComplete = null,
        CancellationToken ct = default)
    {
        _writer      = writer;
        _contentType = contentType;
        _fileName    = fileName;
        _onComplete  = onComplete;
        _ct          = ct;
    }

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.ContentType = _contentType;
        response.Headers["Content-Disposition"] = $"attachment; filename=\"{_fileName}\"";
        // Combine the caller-supplied token with the request-abort token.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            _ct, context.HttpContext.RequestAborted);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rowCount = await _writer(response.Body, linked.Token);
        sw.Stop();
        if (_onComplete is not null)
        {
            try { await _onComplete(rowCount, sw.ElapsedMilliseconds); }
            catch { /* best effort — do not fail the response */ }
        }
    }
}
