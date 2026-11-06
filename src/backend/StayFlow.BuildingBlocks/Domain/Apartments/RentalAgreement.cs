using StayFlow.BuildingBlocks.Common;
using StayFlow.BuildingBlocks.Domain.Inventory;

namespace StayFlow.BuildingBlocks.Domain.Apartments;

public enum AgreementStatus
{
    Draft = 1,
    Active = 2,
    Terminated = 3,
    Expired = 4
}

public class RentalAgreement : TenantEntity<Guid>
{
    public Guid PropertyId { get; private set; }
    public string AgreementNumber { get; private set; } = string.Empty;
    public Guid TenantId { get; private set; }
    public Tenant? Tenant { get; private set; }
    public Guid RoomId { get; private set; }
    public Room? Room { get; private set; }

    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public decimal MonthlyRent { get; private set; }
    public decimal SecurityDeposit { get; private set; }
    public decimal DepositPaid { get; private set; }
    public int BillingCycleDay { get; private set; } = 1; // 1 to 31
    public AgreementStatus Status { get; private set; } = AgreementStatus.Draft;
    public string? Notes { get; private set; }

    // Termination details
    public DateTimeOffset? TerminatedAtUtc { get; private set; }
    public string? TerminationReason { get; private set; }
    public decimal DepositRefundedAmount { get; private set; }
    public decimal DepositDeductedAmount { get; private set; }

    private RentalAgreement() { } // EF Core

    public static RentalAgreement Create(
        Guid organizationId,
        Guid propertyId,
        string agreementNumber,
        Guid tenantId,
        Guid roomId,
        DateOnly startDate,
        DateOnly endDate,
        decimal monthlyRent,
        decimal securityDeposit,
        int billingCycleDay = 1,
        string? notes = null)
    {
        if (endDate <= startDate)
            throw new InvalidOperationException("End date must be greater than start date.");

        if (monthlyRent < 0)
            throw new ArgumentOutOfRangeException(nameof(monthlyRent), "Monthly rent cannot be negative.");

        if (securityDeposit < 0)
            throw new ArgumentOutOfRangeException(nameof(securityDeposit), "Security deposit cannot be negative.");

        if (billingCycleDay < 1 || billingCycleDay > 31)
            throw new ArgumentOutOfRangeException(nameof(billingCycleDay), "Billing cycle day must be between 1 and 31.");

        return new RentalAgreement
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            AgreementNumber = agreementNumber.Trim().ToUpperInvariant(),
            TenantId = tenantId,
            RoomId = roomId,
            StartDate = startDate,
            EndDate = endDate,
            MonthlyRent = monthlyRent,
            SecurityDeposit = securityDeposit,
            DepositPaid = 0m,
            BillingCycleDay = billingCycleDay,
            Status = AgreementStatus.Draft,
            Notes = notes?.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public void RecordDepositPayment(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Deposit payment must be greater than zero.");

        DepositPaid += amount;
        Touch();
    }

    public void Activate()
    {
        if (Status != AgreementStatus.Draft)
            throw new InvalidOperationException($"Cannot activate agreement in '{Status}' status.");

        Status = AgreementStatus.Active;
        Touch();
    }

    public void Renew(DateOnly newEndDate, decimal? newMonthlyRent = null)
    {
        if (Status != AgreementStatus.Active)
            throw new InvalidOperationException("Only active agreements can be renewed.");

        if (newEndDate <= EndDate)
            throw new InvalidOperationException("New end date must be after current end date.");

        EndDate = newEndDate;
        if (newMonthlyRent.HasValue)
        {
            if (newMonthlyRent.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(newMonthlyRent));
            MonthlyRent = newMonthlyRent.Value;
        }

        Touch();
    }

    public void Terminate(string reason, decimal refundAmount, decimal deductionAmount, DateTimeOffset terminatedAt)
    {
        if (Status != AgreementStatus.Active)
            throw new InvalidOperationException($"Only active agreements can be terminated (Current: {Status}).");

        if (refundAmount < 0 || deductionAmount < 0)
            throw new ArgumentException("Refund and deduction amounts cannot be negative.");

        if (refundAmount + deductionAmount > DepositPaid)
            throw new InvalidOperationException($"Total refund ({refundAmount:N2}) + deduction ({deductionAmount:N2}) exceeds deposit paid ({DepositPaid:N2}).");

        Status = AgreementStatus.Terminated;
        TerminationReason = reason?.Trim();
        DepositRefundedAmount = refundAmount;
        DepositDeductedAmount = deductionAmount;
        TerminatedAtUtc = terminatedAt;
        Touch();
    }

    public bool OverlapsWith(DateOnly start, DateOnly end)
    {
        // Inclusive calendar dates for apartment agreements
        return StartDate <= end && start <= EndDate;
    }
}
