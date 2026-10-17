using PlateBack.Domain.Common;
using PlateBack.Domain.Entities;

namespace PlateBack.Domain.Tests;

public class UserTests
{
    private static User NewUser(int reliability = 100)
    {
        var user = User.Create(Guid.NewGuid(), "Maya Rai", TestData.Now);
        user.SetReliabilityForSeed(reliability);
        return user;
    }

    [Fact]
    public void New_users_start_fully_reliable()
    {
        User.Create(Guid.NewGuid(), "  Maya Rai ", TestData.Now).ShouldSatisfyAllConditions(
            u => u.ReliabilityScore.ShouldBe(100),
            u => u.DisplayName.ShouldBe("Maya Rai"));
    }

    [Theory]
    [InlineData("M")]
    [InlineData("")]
    public void Rejects_names_that_are_too_short(string name)
    {
        var error = Should.Throw<DomainException>(() => User.Create(Guid.NewGuid(), name, TestData.Now));
        error.Code.ShouldBe(ErrorCodes.ValidationError);
        error.Field.ShouldBe("displayName");
    }

    [Fact]
    public void Collecting_adds_two_but_never_passes_100()
    {
        var user = NewUser(99);
        user.ApplyCollected(TestData.Rules);
        user.ReliabilityScore.ShouldBe(100);
    }

    [Fact]
    public void A_no_show_takes_twenty_but_never_goes_below_zero()
    {
        var user = NewUser(15);
        user.ApplyNoShow(TestData.Rules);
        user.ReliabilityScore.ShouldBe(0);
    }

    [Fact]
    public void A_late_cancellation_takes_five()
    {
        var user = NewUser(90);
        user.ApplyLateCancel(TestData.Rules);
        user.ReliabilityScore.ShouldBe(85);
    }

    [Theory]
    [InlineData(100, 3)]
    [InlineData(60, 3)]
    [InlineData(59, 1)]
    [InlineData(0, 1)]
    public void Low_reliability_allows_one_active_reservation(int reliability, int limit) =>
        NewUser(reliability).ActiveReservationLimit(TestData.Rules).ShouldBe(limit);

    [Fact]
    public void Suspending_needs_a_reason()
    {
        Should.Throw<DomainException>(() => NewUser().Suspend(" ")).Field.ShouldBe("reason");
    }
}
