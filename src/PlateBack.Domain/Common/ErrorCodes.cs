namespace PlateBack.Domain.Common;

/// <summary>
/// Machine-readable error codes returned in ProblemDetails.code (design doc 6.4).
/// The frontend switches on these strings, so they must not change.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string TokenExpired = "TOKEN_EXPIRED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotOrgMember = "NOT_ORG_MEMBER";
    public const string OrgNotVerified = "ORG_NOT_VERIFIED";
    public const string AccountSuspended = "ACCOUNT_SUSPENDED";
    public const string NotFound = "NOT_FOUND";
    public const string SoldOut = "SOLD_OUT";
    public const string LimitReached = "LIMIT_REACHED";
    public const string WindowClosed = "WINDOW_CLOSED";
    public const string AlreadyCollected = "ALREADY_COLLECTED";
    public const string CodeAlreadyUsed = "CODE_ALREADY_USED";
    public const string InvalidState = "INVALID_STATE";
    public const string Conflict = "CONFLICT";
    public const string RateLimited = "RATE_LIMITED";
    public const string InternalError = "INTERNAL_ERROR";
}
