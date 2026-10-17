using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

using PlateBack.Application.Features.Auth;
using PlateBack.Application.Features.Users;

namespace PlateBack.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers every use-case handler (classes named *Handler or *Job) and
    /// every validator. Plain classes instead of a mediator (decision 1).
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var handlers = typeof(DependencyInjection).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true }
                && (t.Name.EndsWith("Handler", StringComparison.Ordinal) || t.Name.EndsWith("Job", StringComparison.Ordinal)));
        foreach (var handler in handlers)
        {
            services.AddScoped(handler);
        }
        services.AddScoped<CurrentUserReader>();
        services.AddScoped<AuthSession>();
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: false);
        return services;
    }
}
