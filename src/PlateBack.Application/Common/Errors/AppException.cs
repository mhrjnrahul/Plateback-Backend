using PlateBack.Domain.Common;

namespace PlateBack.Application.Common.Errors;

/// <summary>
/// A failure the API reports to the client as RFC 9457 ProblemDetails with
/// the given status and <see cref="Code"/> (design doc 6.4).
/// </summary>
public sealed class AppException(
    int status,
    string code,
    string title,
    string? detail = null,
    IReadOnlyDictionary<string, string[]>? errors = null)
    : Exception(detail ?? title)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public string Title { get; } = title;
    public string? Detail { get; } = detail;
    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;

    public static AppException NotFound(string what) =>
        new(404, ErrorCodes.NotFound, $"{what} not found");

    public static AppException Unauthenticated(string detail = "Log in to continue.") =>
        new(401, ErrorCodes.Unauthenticated, "Not signed in", detail);

    public static AppException NotMember() =>
        new(403, ErrorCodes.NotOrgMember, "Forbidden", "You are not a member of this organization.");

    public static AppException Forbidden(string detail) =>
        new(403, ErrorCodes.Forbidden, "Forbidden", detail);

    public static AppException Suspended() =>
        new(403, ErrorCodes.AccountSuspended, "Account suspended", "This account has been suspended. Contact support.");

    public static AppException Conflict(string code, string title, string detail) =>
        new(409, code, title, detail);

    public static AppException Validation(string field, string message) =>
        new(400, ErrorCodes.ValidationError, "Validation failed", null,
            new Dictionary<string, string[]> { [field] = [message] });
}

/// <summary>
/// A unique index was violated on save. Infrastructure raises it so use cases
/// can react without knowing the database driver.
/// </summary>
public sealed class UniqueConstraintException(string? constraintName, Exception inner)
    : Exception($"Unique constraint {constraintName} was violated.", inner)
{
    public string? ConstraintName { get; } = constraintName;
}
