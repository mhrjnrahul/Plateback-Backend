using FluentValidation;

using PlateBack.Domain.Enums;

namespace PlateBack.Application.Features.Organizations;

public sealed class OrganizationRequestValidator : AbstractValidator<OrganizationRequest>
{
    public OrganizationRequestValidator()
    {
        RuleFor(x => x.Name)
            .Must(n => n?.Trim().Length is >= 2 and <= 100)
            .WithMessage("Enter the business name (2-100 characters).");
        RuleFor(x => x.Type)
            .Must(t => Enum.TryParse<BusinessType>(t, ignoreCase: true, out _))
            .WithMessage("Choose the type of business.");
        RuleFor(x => x.Address)
            .Must(a => a?.Trim().Length is >= 5 and <= 200)
            .WithMessage("Enter the full address customers will come to.");
        RuleFor(x => x.Latitude)
            .NotNull().WithMessage("Set the location.")
            .InclusiveBetween(-90, 90).WithMessage("Latitude must be between -90 and 90.");
        RuleFor(x => x.Longitude)
            .NotNull().WithMessage("Set the location.")
            .InclusiveBetween(-180, 180).WithMessage("Longitude must be between -180 and 180.");
        RuleFor(x => x.Phone).MaximumLength(30);
    }
}

public sealed class AddMemberValidator : AbstractValidator<AddMemberRequest>
{
    public AddMemberValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("Enter a valid email address.");
    }
}
