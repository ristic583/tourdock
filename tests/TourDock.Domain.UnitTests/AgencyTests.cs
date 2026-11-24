using AwesomeAssertions;
using TourDock.Domain.Agencies;

namespace TourDock.Domain.UnitTests;

public sealed class AgencyTests
{
    [Fact]
    public void New_agency_needs_approval_and_owner_is_manager()
    {
        var owner = Guid.NewGuid();

        var agency = Agency.Register("Blue Coast Charters", "hello@example.com", owner).Value;

        agency.Status.Should().Be(AgencyStatus.PendingApproval);
        agency.IsManager(owner).Should().BeTrue();
    }
}
