using PlateBack.Domain.Common;
using PlateBack.Domain.Entities;
using PlateBack.Domain.Enums;

namespace PlateBack.Domain.Tests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    public static readonly BusinessRules Rules = new();

    public static ListingDetails Details(
        int quantity = 10, int price = 150, int originalPrice = 400,
        TimeSpan? startIn = null, TimeSpan? length = null, string title = "Veg thali set") =>
        new(title, ListingCategory.Meals, "Rice and dal.", quantity, originalPrice, price,
            Now + (startIn ?? TimeSpan.FromHours(1)),
            Now + (startIn ?? TimeSpan.FromHours(1)) + (length ?? TimeSpan.FromHours(2)),
            "Jhamsikhel, Lalitpur", 27.68, 85.31, "Contains milk.", null);

    public static Listing Listing(int quantity = 10, TimeSpan? startIn = null, TimeSpan? length = null) =>
        Entities.Listing.Create(Guid.NewGuid(), Details(quantity, startIn: startIn, length: length), Now, Rules);
}
