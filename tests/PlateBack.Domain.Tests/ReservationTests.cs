using PlateBack.Domain.Common;
using PlateBack.Domain.Entities;
using PlateBack.Domain.Enums;

namespace PlateBack.Domain.Tests;

public class ReservationTests
{
    private static (Listing Listing, Reservation Reservation) Reserve()
    {
        var listing = TestData.Listing();
        return (listing, Reservation.Create(listing, Guid.NewGuid(), 1, "K7M2-9QXA", TestData.Now));
    }

    [Fact]
    public void A_new_reservation_expires_when_the_pickup_window_ends()
    {
        var (listing, reservation) = Reserve();
        reservation.Status.ShouldBe(ReservationStatus.Reserved);
        reservation.ExpiresAt.ShouldBe(listing.PickupEnd);
    }

    [Fact]
    public void Cancelling_before_the_window_opens_is_free()
    {
        var (listing, reservation) = Reserve();
        reservation.Cancel(listing.PickupStart.AddSeconds(-1), listing.PickupStart).ShouldBeFalse();
        reservation.Status.ShouldBe(ReservationStatus.Cancelled);
    }

    [Fact]
    public void Cancelling_once_the_window_has_opened_is_late()
    {
        var (listing, reservation) = Reserve();
        reservation.Cancel(listing.PickupStart, listing.PickupStart).ShouldBeTrue();
        reservation.LateCancel.ShouldBeTrue();
    }

    [Fact]
    public void Cannot_cancel_once_the_window_has_ended()
    {
        var (listing, reservation) = Reserve();
        Should.Throw<DomainException>(() => reservation.Cancel(listing.PickupEnd, listing.PickupStart))
            .Code.ShouldBe(ErrorCodes.WindowClosed);
    }

    [Fact]
    public void A_collected_reservation_cannot_be_cancelled()
    {
        var (listing, reservation) = Reserve();
        reservation.Collect(Guid.NewGuid(), TestData.Now);
        Should.Throw<DomainException>(() => reservation.Cancel(TestData.Now, listing.PickupStart))
            .Code.ShouldBe(ErrorCodes.AlreadyCollected);
    }

    [Fact]
    public void A_code_cannot_be_used_twice()
    {
        var (_, reservation) = Reserve();
        reservation.Collect(Guid.NewGuid(), TestData.Now);
        Should.Throw<DomainException>(() => reservation.Collect(Guid.NewGuid(), TestData.Now))
            .Code.ShouldBe(ErrorCodes.CodeAlreadyUsed);
    }

    [Fact]
    public void A_cancelled_reservation_cannot_be_collected()
    {
        var (listing, reservation) = Reserve();
        reservation.Cancel(TestData.Now, listing.PickupStart);
        Should.Throw<DomainException>(() => reservation.Collect(Guid.NewGuid(), TestData.Now))
            .Code.ShouldBe(ErrorCodes.WindowClosed);
    }

    [Fact]
    public void Becomes_a_no_show_only_after_the_window_and_only_once()
    {
        var (listing, reservation) = Reserve();
        reservation.TryMarkNoShow(listing.PickupEnd.AddSeconds(-1)).ShouldBeFalse();
        reservation.TryMarkNoShow(listing.PickupEnd).ShouldBeTrue();
        reservation.TryMarkNoShow(listing.PickupEnd.AddHours(1)).ShouldBeFalse();
        reservation.Status.ShouldBe(ReservationStatus.NoShow);
    }

    [Fact]
    public void Collected_reservations_never_become_no_shows()
    {
        var (listing, reservation) = Reserve();
        reservation.Collect(Guid.NewGuid(), TestData.Now);
        reservation.TryMarkNoShow(listing.PickupEnd.AddHours(1)).ShouldBeFalse();
    }
}
