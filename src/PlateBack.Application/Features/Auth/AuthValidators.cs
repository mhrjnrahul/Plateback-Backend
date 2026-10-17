using FluentValidation;

using PlateBack.Domain.Entities;

namespace PlateBack.Application.Features.Auth;

public sealed class RegisterValidator : AbstractValidator<RegisterRequest>
{
    public RegisterValidator()
    {
        RuleFor(x => x.DisplayName)
            .Must(n => n?.Trim().Length is >= User.DisplayNameMinLength and <= User.DisplayNameMaxLength)
            .WithMessage("Enter your name (2-60 characters).");
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Enter a valid email address.")
            .MaximumLength(256)
            .EmailAddress().WithMessage("Enter a valid email address.");
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Use at least 8 characters.")
            .MinimumLength(8).WithMessage("Use at least 8 characters.")
            .MaximumLength(128);
    }
}

public sealed class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Enter your email address.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Enter your password.");
    }
}
