using FluentValidation;

using Microsoft.Extensions.Options;

using PlateBack.Domain.Common;

namespace PlateBack.Application.Features.Reservations;

public sealed class CreateReservationValidator : AbstractValidator<CreateReservationRequest>
{
    public CreateReservationValidator(IOptions<BusinessRules> rules)
    {
        var max = rules.Value.MaxPerListing;
        RuleFor(x => x.Quantity).InclusiveBetween(1, max).WithMessage($"Choose between 1 and {max}.");
    }
}

public sealed class VerifyPickupValidator : AbstractValidator<VerifyPickupRequest>
{
    public VerifyPickupValidator()
    {
        RuleFor(x => x.PickupCode).NotEmpty().WithMessage("Enter the customer's pickup code.");
    }
}
