using System.ComponentModel.DataAnnotations;

namespace PlateBack.Application.Common.Options;

/// <summary>Token settings ("Jwt" section). The key comes from user secrets or an environment variable.</summary>
public sealed class JwtOptions
{
    public const string Section = "Jwt";

    /// <summary>HMAC-SHA256 signing key, at least 32 characters.</summary>
    [Required, MinLength(32)]
    public string Key { get; set; } = "";

    [Required]
    public string Issuer { get; set; } = "plateback-api";

    [Required]
    public string Audience { get; set; } = "plateback-web";

    [Range(1, 60)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 30)]
    public int RefreshTokenDays { get; set; } = 7;

    /// <summary>
    /// How long a just-rotated refresh token is still accepted (without
    /// rotating again) before reuse counts as theft. Covers two tabs, or a
    /// React double mount, refreshing with the same cookie at once.
    /// </summary>
    [Range(0, 120)]
    public int RefreshReuseGraceSeconds { get; set; } = 30;
}
