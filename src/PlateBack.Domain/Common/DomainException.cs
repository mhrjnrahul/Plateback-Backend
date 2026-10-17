namespace PlateBack.Domain.Common;

/// <summary>
/// A business rule was broken. The API turns it into a 409 response (or 400
/// for <see cref="ErrorCodes.ValidationError"/>) carrying <see cref="Code"/>.
/// </summary>
public sealed class DomainException(string code, string message, string? field = null)
    : Exception(message)
{
    public string Code { get; } = code;

    /// <summary>The request field the error belongs to, when there is one.</summary>
    public string? Field { get; } = field;
}
