using FluentValidation;

namespace PlateBack.Api.Extensions;

/// <summary>
/// Runs the FluentValidation validator for the endpoint's <typeparamref name="T"/>
/// argument before the handler. Failures become one 400 VALIDATION_ERROR with
/// every field's messages, keyed by the camelCase JSON field name.
/// </summary>
internal sealed class ValidationFilter<T> : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var argument = context.Arguments.OfType<T>().FirstOrDefault();
        var validator = context.HttpContext.RequestServices.GetService<IValidator<T>>();
        if (argument is null || validator is null)
        {
            return argument is null && typeof(T).IsClass
                ? Problems.Validation(context.HttpContext, new Dictionary<string, string[]> { ["request"] = ["A request body is required."] })
                : await next(context);
        }

        var result = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted);
        if (result.IsValid)
        {
            return await next(context);
        }
        var errors = result.Errors
            .GroupBy(e => CamelCase(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
        return Problems.Validation(context.HttpContext, errors);
    }

    private static string CamelCase(string name) =>
        string.IsNullOrEmpty(name) ? "request" : char.ToLowerInvariant(name[0]) + name[1..];
}

internal static class ValidationFilterExtensions
{
    public static RouteHandlerBuilder Validate<T>(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<ValidationFilter<T>>().ProducesValidationProblem();
}
