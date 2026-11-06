using StayFlow.BuildingBlocks.Common;
using StayFlow.BuildingBlocks.Domain.Billing;

namespace StayFlow.BuildingBlocks.Domain.Apartments;

public enum InvoiceStatus
{
    Draft = 1,
    Issued = 2,
    Paid = 3,
    PartiallyPaid = 4,
    Cancelled = 5
}

public enum InvoiceItemType
{
    Rent = 1,
    Electricity = 2,
    Water = 3,
    CommonFee = 4,
    MaintenanceFee = 5,
    Penalty = 6,
    Other = 7
}

public class MonthlyInvoiceItem : TenantEntity<Guid>
{
    public Guid PropertyId { get; private set; }
    public Guid InvoiceId { get; private set; }
    public InvoiceItemType ItemType { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; } = 1;
    public decimal UnitPrice { get; private set; }
    public decimal Amount { get; private set; }

    private MonthlyInvoiceItem() { } // EF Core

    public static MonthlyInvoiceItem Create(
        Guid organizationId,
        Guid propertyId,
        Guid invoiceId,
        InvoiceItemType itemType,
        string description,
        decimal amount,
        decimal unitPrice = 0,
        decimal quantity = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new MonthlyInvoiceItem
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            InvoiceId = invoiceId,
            ItemType = itemType,
            Description = description.Trim(),
            Quantity = quantity,
            UnitPrice = unitPrice == 0 ? amount : unitPrice,
            Amount = amount,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }
}

public class InvoicePaymentRecord : TenantEntity<Guid>
{
    public Guid PropertyId { get; private set; }
    public Guid InvoiceId { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentMethod Method { get; private set; }
    public string? ReferenceNumber { get; private set; }
    public string? Notes { get; private set; }

    private InvoicePaymentRecord() { } // EF Core

    public static InvoicePaymentRecord Create(
        Guid organizationId,
        Guid propertyId,
        Guid invoiceId,
        decimal amount,
        PaymentMethod method,
        string? referenceNumber = null,
        string? notes = null)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));

        return new InvoicePaymentRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            InvoiceId = invoiceId,
            Amount = amount,
            Method = method,
            ReferenceNumber = referenceNumber?.Trim(),
            Notes = notes?.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }
}

public class MonthlyInvoice : TenantEntity<Guid>
{
    public Guid PropertyId { get; private set; }
    public string InvoiceNumber { get; private set; } = string.Empty;
    public Guid AgreementId { get; private set; }
    public RentalAgreement? Agreement { get; private set; }
    public Guid TenantId { get; private set; }
    public Tenant? Tenant { get; private set; }
    public Guid RoomId { get; private set; }
    public string BillingMonth { get; private set; } = string.Empty; // e.g. "2026-11"
    public DateOnly DueDate { get; private set; }

    public decimal TotalAmount { get; private set; }
    public decimal PaidAmount { get; private set; }
    public decimal Balance => TotalAmount - PaidAmount;
    public InvoiceStatus Status { get; private set; } = InvoiceStatus.Draft;
    public DateTimeOffset? PaidAtUtc { get; private set; }

    private readonly List<MonthlyInvoiceItem> _items = [];
    public IReadOnlyCollection<MonthlyInvoiceItem> Items => _items.AsReadOnly();

    private readonly List<InvoicePaymentRecord> _payments = [];
    public IReadOnlyCollection<InvoicePaymentRecord> Payments => _payments.AsReadOnly();

    private MonthlyInvoice() { } // EF Core

    public static MonthlyInvoice Create(
        Guid organizationId,
        Guid propertyId,
        string invoiceNumber,
        Guid agreementId,
        Guid tenantId,
        Guid roomId,
        string billingMonth,
        DateOnly dueDate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(billingMonth);

        return new MonthlyInvoice
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            InvoiceNumber = invoiceNumber.Trim().ToUpperInvariant(),
            AgreementId = agreementId,
            TenantId = tenantId,
            RoomId = roomId,
            BillingMonth = billingMonth.Trim(),
            DueDate = dueDate,
            TotalAmount = 0m,
            PaidAmount = 0m,
            Status = InvoiceStatus.Draft,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public void AddItem(InvoiceItemType itemType, string description, decimal amount, decimal unitPrice = 0, decimal quantity = 1)
    {
        if (Status == InvoiceStatus.Paid || Status == InvoiceStatus.Cancelled)
            throw new InvalidOperationException($"Cannot add item to invoice in '{Status}' status.");

        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Item amount cannot be negative.");

        var item = MonthlyInvoiceItem.Create(
            OrganizationId,
            PropertyId,
            Id,
            itemType,
            description,
            amount,
            unitPrice,
            quantity);

        _items.Add(item);
        TotalAmount += amount;
        Touch();
    }

    public void Issue()
    {
        if (Status != InvoiceStatus.Draft)
            throw new InvalidOperationException($"Cannot issue invoice in '{Status}' status.");

        Status = InvoiceStatus.Issued;
        Touch();
    }

    public void RecordPayment(decimal amount, PaymentMethod method, string? referenceNumber = null, string? notes = null)
    {
        if (Status == InvoiceStatus.Cancelled)
            throw new InvalidOperationException("Cannot record payment on a cancelled invoice.");

        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Payment amount must be greater than zero.");

        if (amount > Balance)
            throw new InvalidOperationException($"Payment amount ({amount:N2}) exceeds outstanding balance ({Balance:N2}).");

        var record = InvoicePaymentRecord.Create(
            OrganizationId,
            PropertyId,
            Id,
            amount,
            method,
            referenceNumber,
            notes);

        _payments.Add(record);
        PaidAmount += amount;

        if (Balance == 0)
        {
            Status = InvoiceStatus.Paid;
            PaidAtUtc = DateTimeOffset.UtcNow;
        }
        else
        {
            Status = InvoiceStatus.PartiallyPaid;
        }

        Touch();
    }

    public void Cancel(string reason)
    {
        if (Status == InvoiceStatus.Paid)
            throw new InvalidOperationException("Cannot cancel an invoice that is already paid.");

        Status = InvoiceStatus.Cancelled;
        Touch();
    }
}
