using PlateBack.Domain.Common;
using PlateBack.Domain.Entities;
using PlateBack.Domain.Enums;

namespace PlateBack.Domain.Tests;

public class ListingTests
{
    [Fact]
    public void A_new_listing_is_active_with_all_stock_available()
    {
        var listing = TestData.Listing(quantity: 6);
        listing.Status.ShouldBe(ListingStatus.Active);
        listing.QuantityAvailable.ShouldBe(6);
    }

    [Theory]
    [InlineData(14, false)]
    [InlineData(15, true)]
    [InlineData(720, true)]
    [InlineData(721, false)]
    public void The_pickup_window_must_last_15_minutes_to_12_hours(int minutes, bool allowed)
    {
        var create = () => Listing.Create(Guid.NewGuid(), TestData.Details(length: TimeSpan.FromMinutes(minutes)), TestData.Now, TestData.Rules);
        if (allowed)
        {
            create.ShouldNotThrow();
        }
        else
        {
            Should.Throw<DomainException>(create).Field.ShouldBe("pickupEnd");
        }
    }

    [Fact]
    public void The_window_must_end_in_the_future()
    {
        var details = TestData.Details(startIn: TimeSpan.FromHours(-3), length: TimeSpan.FromHours(2));
        Should.Throw<DomainException>(() => Listing.Create(Guid.NewGuid(), details, TestData.Now, TestData.Rules))
            .Message.ShouldContain("future");
    }

    [Fact]
    public void Price_cannot_be_above_the_original_price()
    {
        Should.Throw<DomainException>(() =>
                Listing.Create(Guid.NewGuid(), TestData.Details(price: 500, originalPrice: 400), TestData.Now, TestData.Rules))
            .Field.ShouldBe("price");
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("")]
    public void Titles_must_be_3_to_80_characters(string title)
    {
        Should.Throw<DomainException>(() =>
                Listing.Create(Guid.NewGuid(), TestData.Details(title: title), TestData.Now, TestData.Rules))
            .Field.ShouldBe("title");
    }

    [Fact]
    public void Reserving_more_than_is_left_is_sold_out()
    {
        var listing = TestData.Listing(quantity: 1);
        Should.Throw<DomainException>(() => listing.EnsureCanReserve(2, TestData.Now)).Code.ShouldBe(ErrorCodes.SoldOut);
    }

    [Fact]
    public void Reserving_after_the_window_ended_is_window_closed()
    {
        var listing = TestData.Listing();
        Should.Throw<DomainException>(() => listing.EnsureCanReserve(1, listing.PickupEnd))
            .Code.ShouldBe(ErrorCodes.WindowClosed);
    }

    [Fact]
    public void Editing_cannot_drop_quantity_below_what_is_reserved()
    {
        var listing = TestData.Listing(quantity: 10);
        listing.SetStockForSeed(available: 4, ListingStatus.Active); // 6 reserved
        Should.Throw<DomainException>(() => listing.Update(TestData.Details(quantity: 5), TestData.Now, TestData.Rules))
            .Field.ShouldBe("quantityTotal");
    }

    [Fact]
    public void Editing_to_exactly_what_is_reserved_sells_out()
    {
        var listing = TestData.Listing(quantity: 10);
        listing.SetStockForSeed(available: 4, ListingStatus.Active);
        listing.Update(TestData.Details(quantity: 6), TestData.Now, TestData.Rules);
        listing.ShouldSatisfyAllConditions(
            l => l.QuantityAvailable.ShouldBe(0),
            l => l.Status.ShouldBe(ListingStatus.SoldOut));
    }

    [Fact]
    public void Expiry_happens_only_once_the_window_has_ended()
    {
        var listing = TestData.Listing();
        listing.TryExpire(listing.PickupEnd.AddMinutes(-1)).ShouldBeFalse();
        listing.TryExpire(listing.PickupEnd).ShouldBeTrue();
        listing.Status.ShouldBe(ListingStatus.Expired);
        listing.TryExpire(listing.PickupEnd.AddHours(1)).ShouldBeFalse();
    }

    [Fact]
    public void Cancelled_and_expired_listings_stay_that_way()
    {
        var listing = TestData.Listing();
        listing.Cancel(TestData.Now);
        Should.Throw<DomainException>(() => listing.Cancel(TestData.Now)).Code.ShouldBe(ErrorCodes.InvalidState);
        listing.TryExpire(listing.PickupEnd).ShouldBeFalse();
        Should.Throw<DomainException>(() => listing.EnsureCanReserve(1, TestData.Now)).Code.ShouldBe(ErrorCodes.WindowClosed);
    }

    [Fact]
    public void Removing_needs_a_reason()
    {
        Should.Throw<DomainException>(() => TestData.Listing().Remove("", TestData.Now)).Field.ShouldBe("reason");
    }
}
