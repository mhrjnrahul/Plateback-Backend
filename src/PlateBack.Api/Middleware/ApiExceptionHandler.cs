using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

using PlateBack.Api.Extensions;
using PlateBack.Application.Common.Errors;
using PlateBack.Domain.Common;

namespace PlateBack.Api.Middleware;

/// <summary>
/// Turns exceptions into ProblemDetails responses with the right status and
/// code. Unexpected errors become a 500 without internal details; they are
/// logged with the request's trace id so they can be found.
/// </summary>
internal sealed partial class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        switch (exception)
        {
            case AppException app:
                await Problems.WriteAsync(context, app.Status, app.Code, app.Title, app.Detail, app.Errors);
                break;

            case DomainException domain when domain.Code == ErrorCodes.ValidationError:
                await Problems.WriteAsync(context, 400, domain.Code, "Validation failed", null,
                    new Dictionary<string, string[]> { [domain.Field ?? "request"] = [domain.Message] });
                break;

            case DomainException domain when domain.Code == ErrorCodes.NotFound:
                await Problems.WriteAsync(context, 404, domain.Code, "Not found", domain.Message);
                break;

            case DomainException domain:
                await Problems.WriteAsync(context, 409, domain.Code, Titles.GetValueOrDefault(domain.Code, "Conflict"), domain.Message);
                break;

            case DbUpdateConcurrencyException:
                await Problems.WriteAsync(context, 409, ErrorCodes.Conflict, "Changed by someone else",
                    "This was changed by another request at the same time. Reload and try again.");
                break;

            case UniqueConstraintException:
                await Problems.WriteAsync(context, 409, ErrorCodes.Conflict, "Already exists",
                    "This conflicts with existing data. Reload and try again.");
                break;

            case BadHttpRequestException bad:
                await Problems.WriteAsync(context, bad.StatusCode, ErrorCodes.ValidationError, "Bad request",
                    bad.StatusCode == StatusCodes.Status413PayloadTooLarge
                        ? "The request is too large."
                        : "The request could not be read. Check the JSON body and parameter types.");
                break;

            case OperationCanceledException when context.RequestAborted.IsCancellationRequested:
                // The client went away; nothing to send.
                return true;

            default:
                LogUnhandled(logger, exception, context.Request.Method, context.Request.Path);
                await Problems.WriteAsync(context, 500, ErrorCodes.InternalError, "Something went wrong",
                    "An unexpected error occurred. Quote the trace id if you report it.");
                break;
        }
        return true;
    }

    private static readonly Dictionary<string, string> Titles = new()
    {
        [ErrorCodes.SoldOut] = "No longer available",
        [ErrorCodes.LimitReached] = "Limit reached",
        [ErrorCodes.WindowClosed] = "Pickup window closed",
        [ErrorCodes.AlreadyCollected] = "Already collected",
        [ErrorCodes.CodeAlreadyUsed] = "Code already used",
        [ErrorCodes.InvalidState] = "Not allowed now",
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, string path);
}
