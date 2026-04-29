using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Domain.Workflow;

namespace BomPriceApproval.Tests.Workflow;

public class RequisitionStateMachineTests
{
    // Happy path transitions
    [Theory]
    [InlineData(RequisitionStatus.Draft, RequisitionStatus.Costing)]
    [InlineData(RequisitionStatus.Costing, RequisitionStatus.MdPricing)]
    [InlineData(RequisitionStatus.MdPricing, RequisitionStatus.CustomerConfirm)]
    [InlineData(RequisitionStatus.MdPricing, RequisitionStatus.Rejected)]
    [InlineData(RequisitionStatus.CustomerConfirm, RequisitionStatus.MdFinalSign)]
    [InlineData(RequisitionStatus.CustomerConfirm, RequisitionStatus.MdPricing)]
    [InlineData(RequisitionStatus.MdFinalSign, RequisitionStatus.Signed)]
    public void CanTransition_HappyPath_ReturnsTrue(RequisitionStatus from, RequisitionStatus to)
    {
        Assert.True(RequisitionStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(RequisitionStatus.Draft)]
    [InlineData(RequisitionStatus.Costing)]
    [InlineData(RequisitionStatus.MdPricing)]
    [InlineData(RequisitionStatus.CustomerConfirm)]
    [InlineData(RequisitionStatus.MdFinalSign)]
    public void CanTransition_AnyNonTerminalToCancelled_ReturnsTrue(RequisitionStatus from)
    {
        Assert.True(RequisitionStateMachine.CanTransition(from, RequisitionStatus.Cancelled));
    }

    [Theory]
    [InlineData(RequisitionStatus.Draft, RequisitionStatus.MdPricing)]
    [InlineData(RequisitionStatus.Costing, RequisitionStatus.Signed)]
    [InlineData(RequisitionStatus.Signed, RequisitionStatus.MdFinalSign)]
    [InlineData(RequisitionStatus.Signed, RequisitionStatus.Cancelled)]
    [InlineData(RequisitionStatus.Cancelled, RequisitionStatus.Draft)]
    [InlineData(RequisitionStatus.Rejected, RequisitionStatus.MdPricing)]
    public void CanTransition_Forbidden_ReturnsFalse(RequisitionStatus from, RequisitionStatus to)
    {
        Assert.False(RequisitionStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(RequisitionStatus.Signed, true)]
    [InlineData(RequisitionStatus.Rejected, true)]
    [InlineData(RequisitionStatus.Cancelled, true)]
    [InlineData(RequisitionStatus.Draft, false)]
    [InlineData(RequisitionStatus.Costing, false)]
    [InlineData(RequisitionStatus.MdPricing, false)]
    [InlineData(RequisitionStatus.CustomerConfirm, false)]
    [InlineData(RequisitionStatus.MdFinalSign, false)]
    public void IsTerminal_ReturnsExpected(RequisitionStatus status, bool expected)
    {
        Assert.Equal(expected, RequisitionStateMachine.IsTerminal(status));
    }

    [Theory]
    [InlineData(RequisitionStatus.Signed, new[] { (int)RequisitionStatus.MdFinalSign })]
    [InlineData(RequisitionStatus.MdFinalSign, new[] { (int)RequisitionStatus.CustomerConfirm })]
    [InlineData(RequisitionStatus.CustomerConfirm, new[] { (int)RequisitionStatus.MdPricing })]
    [InlineData(RequisitionStatus.MdPricing, new[] { (int)RequisitionStatus.Costing })]
    [InlineData(RequisitionStatus.Costing, new[] { (int)RequisitionStatus.Draft })]
    public void AdminRollbackTargets_ReturnsExpected(RequisitionStatus from, int[] expected)
    {
        var actual = RequisitionStateMachine.AdminRollbackTargets(from)
            .Select(s => (int)s).ToArray();
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(RequisitionStatus.Cancelled)]
    [InlineData(RequisitionStatus.Rejected)]
    [InlineData(RequisitionStatus.Draft)]
    public void AdminRollbackTargets_TerminalOrInitial_ReturnsEmpty(RequisitionStatus from)
    {
        Assert.Empty(RequisitionStateMachine.AdminRollbackTargets(from));
    }
}
