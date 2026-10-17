using Microsoft.AspNetCore.Identity;

namespace PlateBack.Infrastructure.Identity;

/// <summary>
/// Login data managed by ASP.NET Core Identity: email, password hash and
/// lockout. Shares its id with the domain User (decision 3).
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>;
