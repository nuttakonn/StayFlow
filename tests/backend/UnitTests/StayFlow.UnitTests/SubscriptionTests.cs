using StayFlow.BuildingBlocks.Domain.Subscriptions;

namespace StayFlow.UnitTests;

public class SubscriptionTests
{
    [Fact]
    public void StartTrial_SetsTrialingStatus_AndTrialEndDate()
    {
        var planId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        var subscription = Subscription.StartTrial(orgId, planId, trialDays: 14);

        Assert.Equal(SubscriptionStatus.Trialing, subscription.Status);
        Assert.Equal(orgId, subscription.OrganizationId);
        Assert.Equal(planId, subscription.SubscriptionPlanId);
        Assert.NotNull(subscription.TrialEndsAtUtc);
        Assert.True(subscription.TrialEndsAtUtc > DateTimeOffset.UtcNow.AddDays(13));
    }

    [Fact]
    public void StartTrial_WithZeroTrialDays_HasNoTrialEndDate()
    {
        var subscription = Subscription.StartTrial(Guid.NewGuid(), Guid.NewGuid(), trialDays: 0);

        Assert.Null(subscription.TrialEndsAtUtc);
    }

    [Fact]
    public void IsTrialExpired_BeforeTrialEnd_ReturnsFalse()
    {
        var subscription = Subscription.StartTrial(Guid.NewGuid(), Guid.NewGuid(), trialDays: 14);

        Assert.False(subscription.IsTrialExpired(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsTrialExpired_AfterTrialEnd_ReturnsTrue()
    {
        var subscription = Subscription.StartTrial(Guid.NewGuid(), Guid.NewGuid(), trialDays: 14);

        Assert.True(subscription.IsTrialExpired(DateTimeOffset.UtcNow.AddDays(15)));
    }

    [Fact]
    public void IsEffectivelyActive_TrialingBeforeExpiry_ReturnsTrue()
    {
        var subscription = Subscription.StartTrial(Guid.NewGuid(), Guid.NewGuid(), trialDays: 14);

        Assert.True(subscription.IsEffectivelyActive(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsEffectivelyActive_TrialingAfterExpiry_ReturnsFalse()
    {
        var subscription = Subscription.StartTrial(Guid.NewGuid(), Guid.NewGuid(), trialDays: 14);

        Assert.False(subscription.IsEffectivelyActive(DateTimeOffset.UtcNow.AddDays(15)));
    }

    [Fact]
    public void IsEffectivelyActive_Cancelled_ReturnsFalse()
    {
        var subscription = Subscription.StartTrial(Guid.NewGuid(), Guid.NewGuid(), trialDays: 14);
        subscription.Cancel("Customer requested cancellation");

        Assert.False(subscription.IsEffectivelyActive(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsEffectivelyActive_PastDue_StillGrantsAccess_AsGracePeriod()
    {
        var subscription = Subscription.StartTrial(Guid.NewGuid(), Guid.NewGuid(), trialDays: 14);
        subscription.Activate();
        subscription.MarkPastDue();

        Assert.True(subscription.IsEffectivelyActive(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Cancel_Twice_SecondCallFails()
    {
        var subscription = Subscription.StartTrial(Guid.NewGuid(), Guid.NewGuid(), trialDays: 14);

        var first = subscription.Cancel("First reason");
        var second = subscription.Cancel("Second reason");

        Assert.True(first.IsSuccess);
        Assert.True(second.IsFailure);
        Assert.Equal("Subscription.AlreadyCancelled", second.Error.Code);
    }

    [Fact]
    public void ChangePlan_OnCancelledSubscription_Fails()
    {
        var subscription = Subscription.StartTrial(Guid.NewGuid(), Guid.NewGuid(), trialDays: 14);
        subscription.Cancel("Customer requested cancellation");

        var result = subscription.ChangePlan(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.Cancelled", result.Error.Code);
    }

    [Fact]
    public void ChangePlan_OnActiveSubscription_UpdatesPlanId()
    {
        var subscription = Subscription.StartTrial(Guid.NewGuid(), Guid.NewGuid(), trialDays: 14);
        var newPlanId = Guid.NewGuid();

        var result = subscription.ChangePlan(newPlanId);

        Assert.True(result.IsSuccess);
        Assert.Equal(newPlanId, subscription.SubscriptionPlanId);
    }
}

public class SubscriptionPlanTests
{
    [Fact]
    public void Create_DeduplicatesCapabilityKeys_CaseInsensitively()
    {
        var plan = SubscriptionPlan.Create(
            key: "starter",
            name: "Starter",
            maxUsers: 5,
            maxProperties: 1,
            trialDays: 14,
            capabilityKeys: ["hotel.reservations", "HOTEL.RESERVATIONS", "apartment.rentals"]);

        Assert.Equal(2, plan.Entitlements.Count);
    }

    [Fact]
    public void Create_NormalizesKeyToLowercase()
    {
        var plan = SubscriptionPlan.Create(
            key: "STARTER",
            name: "Starter",
            maxUsers: null,
            maxProperties: null,
            trialDays: 14,
            capabilityKeys: []);

        Assert.Equal("starter", plan.Key);
    }

    [Fact]
    public void Create_WithEmptyKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => SubscriptionPlan.Create(
            key: "",
            name: "Starter",
            maxUsers: null,
            maxProperties: null,
            trialDays: 14,
            capabilityKeys: []));
    }
}
