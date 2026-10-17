using PlateBack.Domain.Common;
using PlateBack.Domain.Entities;
using PlateBack.Domain.Enums;

namespace PlateBack.Domain.Tests;

public class OrganizationTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    private static Organization NewOrganization() =>
        Organization.Create(Guid.NewGuid(), "Sunrise Bakery", BusinessType.Bakery, "Thamel Rd 12", 27.7, 85.3, null, OwnerId, TestData.Now);

    [Fact]
    public void A_new_business_is_pending_and_cannot_publish()
    {
        var organization = NewOrganization();
        organization.Status.ShouldBe(OrganizationStatus.Pending);
        organization.CanPublish.ShouldBeFalse();
        organization.IsOwner(OwnerId).ShouldBeTrue();
    }

    [Fact]
    public void Verifying_allows_publishing()
    {
        var organization = NewOrganization();
        organization.Verify(Guid.NewGuid(), TestData.Now);
        organization.CanPublish.ShouldBeTrue();
        Should.Throw<DomainException>(() => organization.Verify(Guid.NewGuid(), TestData.Now)).Code.ShouldBe(ErrorCodes.InvalidState);
    }

    [Fact]
    public void Rejecting_needs_a_reason()
    {
        Should.Throw<DomainException>(() => NewOrganization().Reject("  ")).Field.ShouldBe("reason");
    }

    [Fact]
    public void Only_verified_businesses_can_be_suspended()
    {
        Should.Throw<DomainException>(() => NewOrganization().Suspend("Complaints")).Code.ShouldBe(ErrorCodes.InvalidState);
    }

    [Fact]
    public void The_last_owner_cannot_be_removed()
    {
        var organization = NewOrganization();
        var staff = Guid.NewGuid();
        organization.AddStaff(staff, TestData.Now);
        organization.RemoveMember(staff);
        Should.Throw<DomainException>(() => organization.RemoveMember(OwnerId)).Code.ShouldBe(ErrorCodes.InvalidState);
    }
}
