namespace PlateBack.Api.Extensions;

/// <summary>
/// The refresh token travels only in this cookie: httpOnly (scripts cannot
/// read it), Secure, SameSite=Strict, and sent only to /api/v1/auth.
/// </summary>
internal static class RefreshCookie
{
    public const string Name = "plateback_refresh";
    public const string Path = "/api/v1/auth";

    public static void Set(HttpResponse response, string token, DateTimeOffset expires) =>
        response.Cookies.Append(Name, token, Options(expires));

    public static void Clear(HttpResponse response) =>
        response.Cookies.Delete(Name, Options(DateTimeOffset.UnixEpoch));

    public static string? Read(HttpRequest request) => request.Cookies[Name];

    private static CookieOptions Options(DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = Path,
        Expires = expires,
        IsEssential = true,
    };
}
