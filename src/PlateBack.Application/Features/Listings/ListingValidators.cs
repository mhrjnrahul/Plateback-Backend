using FluentValidation;

using PlateBack.Domain.Entities;
using PlateBack.Domain.Enums;

namespace PlateBack.Application.Features.Listings;

/// <summary>
/// Shape checks on the request. Rules that depend on the current time or on
/// stock already reserved are checked by the Listing entity.
/// </summary>
public sealed class ListingInputValidator : AbstractValidator<ListingInput>
{
    public ListingInputValidator()
    {
        RuleFor(x => x.Title)
            .Must(t => t?.Trim().Length is >= Listing.TitleMinLength and <= Listing.TitleMaxLength)
            .WithMessage("Title must be 3-80 characters.");
        RuleFor(x => x.Category)
            .Must(c => Enum.TryParse<ListingCategory>(c, ignoreCase: true, out _))
            .WithMessage("Choose a category.");
        RuleFor(x => x.Description).MaximumLength(500).WithMessage("Keep the description under 500 characters.");
        RuleFor(x => x.QuantityTotal)
            .NotNull().WithMessage("Enter how many portions you have.")
            .InclusiveBetween(1, 200).WithMessage("Quantity must be 1-200.");
        RuleFor(x => x.OriginalPrice)
            .NotNull().WithMessage("Enter the MRP.")
            .GreaterThanOrEqualTo(0).WithMessage("Cannot be negative.");
        RuleFor(x => x.Price)
            .NotNull().WithMessage("Enter the selling price (0 to give it away free).")
            .GreaterThanOrEqualTo(0).WithMessage("Cannot be negative.")
            .LessThanOrEqualTo(x => x.OriginalPrice ?? int.MaxValue)
            .WithMessage("Selling price must be equal to or lower than MRP.");
        RuleFor(x => x.PickupStart).NotNull().WithMessage("Choose when pickup starts.");
        RuleFor(x => x.PickupEnd)
            .NotNull().WithMessage("Choose when pickup ends.")
            .GreaterThan(x => x.PickupStart).When(x => x.PickupStart is not null)
            .WithMessage("Pickup must end after it starts.");
        RuleFor(x => x.Address)
            .Must(a => a?.Trim().Length is >= 1 and <= 200)
            .WithMessage("Enter the pickup address.");
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).When(x => x.Latitude is not null);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).When(x => x.Longitude is not null);
        RuleFor(x => x.AllergenNotes)
            .Must(a => a?.Trim().Length is >= 3 and <= 300)
            .WithMessage("Allergen information is required. Write \"None known\" if there are none.");
        RuleFor(x => x.ImageUrl)
            .MaximumLength(500)
            .Must(BeAnUploadOrWebUrl!).When(x => !string.IsNullOrWhiteSpace(x.ImageUrl))
            .WithMessage("Use the URL returned by the photo upload.");
    }

    private static bool BeAnUploadOrWebUrl(string url) =>
        url.StartsWith("/uploads/", StringComparison.Ordinal)
        || (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https");
}

public sealed class SearchListingsValidator : AbstractValidator<SearchListingsQuery>
{
    private static readonly string[] Sorts = ["distance", "pickup", "price"];

    public SearchListingsValidator()
    {
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90).When(x => x.Lat is not null);
        RuleFor(x => x.Lng).InclusiveBetween(-180, 180).When(x => x.Lng is not null);
        RuleFor(x => x).Must(x => (x.Lat is null) == (x.Lng is null))
            .WithName("lat").WithMessage("Send both lat and lng, or neither.");
        RuleFor(x => x.RadiusKm).GreaterThan(0).When(x => x.RadiusKm is not null);
        RuleFor(x => x.Category)
            .Must(c => Enum.TryParse<ListingCategory>(c, ignoreCase: true, out _))
            .When(x => !string.IsNullOrEmpty(x.Category))
            .WithMessage("Unknown category.");
        RuleFor(x => x.MaxPrice).GreaterThanOrEqualTo(0).When(x => x.MaxPrice is not null);
        RuleFor(x => x.Sort)
            .Must(s => Sorts.Contains(s))
            .When(x => !string.IsNullOrEmpty(x.Sort))
            .WithMessage("Sort by distance, pickup or price.");
        RuleFor(x => x.Q).MaximumLength(100);
    }
}
