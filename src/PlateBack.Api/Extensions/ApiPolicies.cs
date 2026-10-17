namespace PlateBack.Api.Extensions;

internal static class AuthPolicies
{
    public const string Admin = "Admin";
}

internal static class RateLimits
{
    /// <summary>Register, login, refresh: per IP address.</summary>
    public const string Auth = "auth";

    /// <summary>Pickup code checks: per signed-in user, against code guessing.</summary>
    public const string PickupCodes = "pickup-codes";
}

internal static class CachePolicies
{
    public const string Impact = "impact";
}
