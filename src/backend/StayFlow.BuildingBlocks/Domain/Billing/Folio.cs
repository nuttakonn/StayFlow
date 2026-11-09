using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.Billing;

public enum FolioStatus
{
    Open = 1,
    Closed = 2
}

public enum FolioItemType
{
    RoomCharge = 1,
    FoodAndBeverage = 2,
    Minibar = 3,
    ExtraBed = 4,
    Laundry = 5,
    DamageOrLoss = 6,
    Adjustment = 7,
    Other = 8,
    AncillaryService = 9,
    Package = 10
}

public enum PaymentMethod
{
    Cash = 1,
    BankTransfer = 2,
    CreditCard = 3,
    PromptPay = 4,
    Other = 5
}

public enum PaymentStatus
{
    Completed = 1,
    Reversed = 2
}

public class Folio : PropertyEntity<Guid>
{
    public Guid ReservationId { get; private set; }
    public string FolioNumber { get; private set; } = string.Empty;
    public FolioStatus Status { get; private set; } = FolioStatus.Open;
    public string Currency { get; private set; } = "THB";
    public string? Notes { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }

    private readonly List<FolioItem> _items = [];
    public IReadOnlyCollection<FolioItem> Items => _items.AsReadOnly();

    private readonly List<Payment> _payments = [];
    public IReadOnlyCollection<Payment> Payments => _payments.AsReadOnly();

    private Folio() { } // EF Core

    public static Folio Create(
        Guid organizationId,
        Guid propertyId,
        Guid reservationId,
        string folioNumber,
        string currency = "THB",
        string? notes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folioNumber);

        return new Folio
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            ReservationId = reservationId,
            FolioNumber = folioNumber.Trim().ToUpperInvariant(),
            Status = FolioStatus.Open,
            Currency = currency,
            Notes = notes?.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public FolioItem AddCharge(
        FolioItemType type,
        string description,
        decimal unitPrice,
        int quantity = 1)
    {
        if (Status != FolioStatus.Open)
            throw new InvalidOperationException("Cannot add charges to a closed folio.");

        if (quantity <= 0)
            throw new ArgumentException("Quantity must be greater than zero.");

        var item = FolioItem.Create(
            OrganizationId,
            PropertyId,
            Id,
            type,
            description,
            unitPrice,
            quantity);

        _items.Add(item);
        Touch();
        return item;
    }

    public Payment RecordPayment(
        PaymentMethod method,
        decimal amount,
        string? referenceNumber = null,
        string? notes = null)
    {
        if (Status != FolioStatus.Open)
            throw new InvalidOperationException("Cannot record payment to a closed folio.");

        if (amount <= 0)
            throw new ArgumentException("Payment amount must be greater than zero.");

        var payment = Payment.Create(
            OrganizationId,
            PropertyId,
            Id,
            method,
            amount,
            Currency,
            referenceNumber,
            notes);

        _payments.Add(payment);
        Touch();
        return payment;
    }

    public void ReversePayment(Guid paymentId, string reason)
    {
        if (Status != FolioStatus.Open)
            throw new InvalidOperationException("Cannot reverse payments in a closed folio.");

        var payment = _payments.FirstOrDefault(p => p.Id == paymentId);
        if (payment == null)
            throw new InvalidOperationException($"Payment with ID '{paymentId}' not found in this folio.");

        payment.Reverse(reason);
        Touch();
    }

    public decimal TotalCharges => _items.Sum(i => i.Amount);

    public decimal TotalPayments => _payments
        .Where(p => p.Status == PaymentStatus.Completed)
        .Sum(p => p.Amount);

    public decimal Balance => TotalCharges - TotalPayments;

    public void Close()
    {
        if (Status == FolioStatus.Closed)
            throw new InvalidOperationException("Folio is already closed.");

        if (Balance != 0m)
            throw new InvalidOperationException($"Cannot close folio with non-zero balance ({Balance} {Currency}). Please settle the outstanding balance first.");

        Status = FolioStatus.Closed;
        ClosedAtUtc = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Reopen()
    {
        if (Status != FolioStatus.Closed)
            throw new InvalidOperationException("Folio is not closed.");

        Status = FolioStatus.Open;
        ClosedAtUtc = null;
        Touch();
    }
}

public class FolioItem : PropertyEntity<Guid>
{
    public Guid FolioId { get; private set; }
    public FolioItemType Type { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public decimal Amount { get; private set; }

    private FolioItem() { } // EF Core

    public static FolioItem Create(
        Guid organizationId,
        Guid propertyId,
        Guid folioId,
        FolioItemType type,
        string description,
        decimal unitPrice,
        int quantity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new FolioItem
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            FolioId = folioId,
            Type = type,
            Description = description.Trim(),
            UnitPrice = unitPrice,
            Quantity = quantity,
            Amount = unitPrice * quantity,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }
}

public class Payment : PropertyEntity<Guid>
{
    public Guid FolioId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "THB";
    public string? ReferenceNumber { get; private set; }
    public string? Notes { get; private set; }
    public PaymentStatus Status { get; private set; } = PaymentStatus.Completed;
    public string? ReversalReason { get; private set; }
    public DateTimeOffset? ReversedAtUtc { get; private set; }

    private Payment() { } // EF Core

    public static Payment Create(
        Guid organizationId,
        Guid propertyId,
        Guid folioId,
        PaymentMethod method,
        decimal amount,
        string currency = "THB",
        string? referenceNumber = null,
        string? notes = null)
    {
        return new Payment
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            FolioId = folioId,
            Method = method,
            Amount = amount,
            Currency = currency,
            ReferenceNumber = referenceNumber?.Trim(),
            Notes = notes?.Trim(),
            Status = PaymentStatus.Completed,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public void Reverse(string reason)
    {
        if (Status == PaymentStatus.Reversed)
            throw new InvalidOperationException("Payment is already reversed.");

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Status = PaymentStatus.Reversed;
        ReversalReason = reason.Trim();
        ReversedAtUtc = DateTimeOffset.UtcNow;
        Touch();
    }
}
