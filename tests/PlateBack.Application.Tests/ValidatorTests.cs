using Microsoft.Extensions.Options;

using PlateBack.Application.Features.Auth;
using PlateBack.Application.Features.Listings;
using PlateBack.Application.Features.Organizations;
using PlateBack.Application.Features.Reservations;
using PlateBack.Domain.Common;

namespace PlateBack.Application.Tests;

public class ValidatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 17, 18, 0, 0, TimeSpan.Zero);

    private static ListingInput Listing(int? price = 150, int? originalPrice = 400, string category = "Meals", string? imageUrl = null) =>
        new("Veg thali set", category, "", 10, originalPrice, price, Start, Start.AddHours(2), "Jhamsikhel", null, null,
            "Contains milk.", imageUrl);

    [Fact]
    public void A_complete_listing_is_valid() =>
        new ListingInputValidator().Validate(Listing()).IsValid.ShouldBeTrue();

    [Fact]
    public void Price_above_MRP_is_reported_on_the_price_field()
    {
        var result = new ListingInputValidator().Validate(Listing(price: 500));
        result.Errors.ShouldContain(e => e.PropertyName == "Price" && e.ErrorMessage.Contains("MRP"));
    }

    [Fact]
    public void Unknown_categories_are_rejected() =>
        new ListingInputValidator().Validate(Listing(category: "Pizza")).Errors
            .ShouldContain(e => e.PropertyName == "Category");

    [Theory]
    [InlineData("/uploads/abc.jpg", true)]
    [InlineData("https://cdn.example.com/a.jpg", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:image/png;base64,AAAA", false)]
    public void Image_urls_must_be_uploads_or_web_addresses(string url, bool valid) =>
        new ListingInputValidator().Validate(Listing(imageUrl: url)).IsValid.ShouldBe(valid);

    [Theory]
    [InlineData("Maya", "maya@example.com", "password123", true)]
    [InlineData("M", "maya@example.com", "password123", false)]
    [InlineData("Maya", "not-an-email", "password123", false)]
    [InlineData("Maya", "maya@example.com", "short", false)]
    public void Registration_rules(string name, string email, string password, bool valid) =>
        new RegisterValidator().Validate(new RegisterRequest(name, email, password)).IsValid.ShouldBe(valid);

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void Customers_reserve_one_or_two_units(int quantity, bool valid) =>
        new CreateReservationValidator(Options.Create(new BusinessRules()))
            .Validate(new CreateReservationRequest(quantity)).IsValid.ShouldBe(valid);

    [Fact]
    public void Business_type_must_be_one_of_the_known_types()
    {
        var request = new OrganizationRequest("Hilltop Kitchen", "Spaceport", "Kupondole, Lalitpur", 27.68, 85.31, null);
        new OrganizationRequestValidator().Validate(request).Errors.ShouldContain(e => e.PropertyName == "Type");
    }

    [Fact]
    public void Search_needs_both_coordinates_or_neither()
    {
        var query = new SearchListingsQuery(27.7, null, null, null, null, null, null, null, null, null, null);
        new SearchListingsValidator().Validate(query).IsValid.ShouldBeFalse();
    }
}
