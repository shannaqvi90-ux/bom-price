using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Authorization;
using FluentAssertions;

namespace BomPriceApproval.Tests.Authorization;

public class AdminOverrideAuthorizationHelperTests
{
    [Theory]
    [InlineData(RequisitionStatus.Approved, RequisitionStatus.MdReview, true)]
    [InlineData(RequisitionStatus.MdReview, RequisitionStatus.CostingPending, true)]
    [InlineData(RequisitionStatus.CostingInProgress, RequisitionStatus.CostingPending, true)]
    [InlineData(RequisitionStatus.CostingPending, RequisitionStatus.BomInProgress, true)]
    [InlineData(RequisitionStatus.BomInProgress, RequisitionStatus.BomPending, true)]
    public void CanRollback_AllowsWhitelistedTransitions(RequisitionStatus from, RequisitionStatus to, bool expected)
        => AdminOverrideAuthorization.CanRollback(from, to).Should().Be(expected);

    [Theory]
    [InlineData(RequisitionStatus.Approved, RequisitionStatus.BomPending)]   // skip-jump
    [InlineData(RequisitionStatus.BomPending, RequisitionStatus.Approved)]   // forward
    [InlineData(RequisitionStatus.MdReview, RequisitionStatus.Approved)]     // forward
    [InlineData(RequisitionStatus.Rejected, RequisitionStatus.MdReview)]     // from rejected
    [InlineData(RequisitionStatus.Rejected, RequisitionStatus.BomPending)]   // from rejected
    [InlineData(RequisitionStatus.Draft, RequisitionStatus.BomPending)]      // not in whitelist
    public void CanRollback_BlocksDisallowedTransitions(RequisitionStatus from, RequisitionStatus to)
        => AdminOverrideAuthorization.CanRollback(from, to).Should().BeFalse();

    [Theory]
    [InlineData(RequisitionStatus.CostingPending, true)]
    [InlineData(RequisitionStatus.CostingInProgress, true)]
    [InlineData(RequisitionStatus.MdReview, true)]
    [InlineData(RequisitionStatus.Approved, false)]
    [InlineData(RequisitionStatus.Rejected, false)]
    [InlineData(RequisitionStatus.BomPending, false)]
    [InlineData(RequisitionStatus.BomInProgress, false)]
    public void CanUnlockBom_OnlyDownstreamStatuses(RequisitionStatus current, bool expected)
        => AdminOverrideAuthorization.CanUnlockBom(current).Should().Be(expected);

    [Theory]
    [InlineData(RequisitionStatus.MdReview, true)]
    [InlineData(RequisitionStatus.Approved, false)]
    [InlineData(RequisitionStatus.Rejected, false)]
    [InlineData(RequisitionStatus.CostingInProgress, false)]
    [InlineData(RequisitionStatus.CostingPending, false)]
    public void CanUnlockCosting_OnlyMdReview(RequisitionStatus current, bool expected)
        => AdminOverrideAuthorization.CanUnlockCosting(current).Should().Be(expected);
}
