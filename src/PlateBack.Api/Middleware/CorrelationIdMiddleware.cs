using Serilog.Context;

namespace PlateBack.Api.Middleware;

/// <summary>
/// Gives every request a correlation id (from the X-Correlation-Id header, or
/// the trace id), adds it to every log line and echoes it in the response.
/// </summary>
internal sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string Header = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var id = context.Request.Headers[Header].FirstOrDefault() is { Length: > 0 and <= 64 } given
            ? given
            : context.TraceIdentifier;
        context.TraceIdentifier = id;
        context.Response.Headers[Header] = id;
        using (LogContext.PushProperty("CorrelationId", id))
        {
            await next(context);
        }
    }
}
