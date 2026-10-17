using System.Text.Json.Serialization;

using PlateBack.Domain.Common;

namespace PlateBack.Api.Extensions;

/// <summary>
/// Every error the API returns has this shape: RFC 9457 ProblemDetails plus a
/// machine-readable <c>code</c> (design doc 6.4), served as application/problem+json.
/// </summary>
internal static class Problems
{
    public sealed record Body(
        string Type,
        string Title,
        int Status,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail,
        string Code,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string[]>? Errors,
        string TraceId);

    public static Body Create(
        HttpContext context, int status, string code, string title, string? detail = null,
        IReadOnlyDictionary<string, string[]>? errors = null) =>
        new($"urn:plateback:error:{code.ToLowerInvariant().Replace('_', '-')}", title, status, detail, code, errors,
            context.TraceIdentifier);

    public static Task WriteAsync(
        HttpContext context, int status, string code, string title, string? detail = null,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(
            Create(context, status, code, title, detail, errors),
            options: null,
            contentType: "application/problem+json",
            cancellationToken: context.RequestAborted);
    }

    public static IResult Validation(HttpContext context, IReadOnlyDictionary<string, string[]> errors) =>
        Results.Json(
            Create(context, StatusCodes.Status400BadRequest, ErrorCodes.ValidationError, "Validation failed", null, errors),
            statusCode: StatusCodes.Status400BadRequest,
            contentType: "application/problem+json");
}
