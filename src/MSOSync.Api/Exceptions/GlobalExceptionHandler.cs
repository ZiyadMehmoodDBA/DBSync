using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MSOSync.Common.Exceptions;

namespace MSOSync.Api.Exceptions;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken ct)
    {
        var (status, error, code, message) = exception switch
        {
            NotFoundException ex                   => (404, "Not Found",             ex.Code,            ex.Message),
            DuplicateEntityException ex            => (409, "Conflict",              ex.Code,            ex.Message),
            MSOSync.Common.Exceptions.ValidationException ex => (400, "Bad Request",    ex.Code,            ex.Message),
            ForbiddenOperationException ex         => (403, "Forbidden",             ex.Code,            ex.Message),
            ConcurrencyException ex                => (409, "Conflict",              ex.Code,            ex.Message),
            UnauthorizedException ex               => (401, "Unauthorized",          ex.Code,            ex.Message),
            FluentValidation.ValidationException e => (400, "Bad Request",           "VALIDATION_ERROR",
                string.Join("; ", e.Errors.Select(x => x.ErrorMessage))),
            _                                      => (500, "Internal Server Error", "INTERNAL_SERVER_ERROR",
                "An unexpected error occurred")
        };

        if (status == 500)
            logger.LogError(exception, "Unhandled exception");

        var correlationId = httpContext.Request.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new
        {
            timestamp = DateTime.UtcNow,
            status,
            error,
            code,
            message,
            correlationId
        }, ct);

        return true;
    }
}
