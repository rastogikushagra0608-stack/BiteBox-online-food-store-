using BiteBox.Domain.Common;
using BiteBox.Domain.Enums;
using BiteBox.Domain.Events;
using BiteBox.Domain.Exceptions;
using BiteBox.Domain.Orders;
using BiteBox.Domain.Pricing;

namespace BiteBox.Domain.Entities;

/// <summary>
/// Order aggregate root. All changes to an order (status, payment) go through methods on this class
/// so that business invariants are enforced in one place regardless of which API endpoint is called.
/// </summary>
public class Order : AuditableEntity
{
    private readonly List<OrderItem> _items = [];
    private readonly List<OrderStatusChange> _statusHistory = [];

    private Order() { } // EF Core

    public string OrderNumber { get; private set; } = string.Empty;
    public int CustomerId { get; private set; }
    public string CustomerName { get; private set; } = string.Empty;
    public string CustomerPhone { get; private set; } = string.Empty;

    public OrderStatus Status { get; private set; }
    public FulfillmentType FulfillmentType { get; private set; }

    /// <summary>Snapshot of the delivery address at order time (null for pickup).</summary>
    public DeliveryAddress? DeliveryAddress { get; private set; }

    public decimal Subtotal { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal DeliveryFee { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal Total { get; private set; }

    public int? CouponId { get; private set; }
    public string? CouponCode { get; private set; }

    public string? Notes { get; private set; }
    public string? CancellationReason { get; private set; }

    /// <summary>Client-generated key; a retried checkout with the same key returns the same order.</summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    public DateTime PlacedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    public Payment Payment { get; private set; } = null!;
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();
    public IReadOnlyCollection<OrderStatusChange> StatusHistory => _statusHistory.AsReadOnly();

    public static Order Create(
        string orderNumber,
        int customerId,
        string customerName,
        string customerPhone,
        FulfillmentType fulfillment,
        DeliveryAddress? address,
        PaymentMethod paymentMethod,
        IReadOnlyCollection<PriceLine> lines,
        PriceBreakdown price,
        Coupon? coupon,
        string? notes,
        string idempotencyKey,
        DateTime now)
    {
        if (lines.Count == 0) throw new DomainException("order.empty", "Cannot place an empty order.");
        if (fulfillment == FulfillmentType.Delivery && address is null)
            throw new DomainException("order.address_required", "A delivery address is required for home delivery.");

        // Online orders wait for payment confirmation; cash orders are placed immediately.
        var initialStatus = paymentMethod == PaymentMethod.CashOnDelivery ? OrderStatus.Placed : OrderStatus.AwaitingPayment;

        var order = new Order
        {
            OrderNumber = orderNumber,
            CustomerId = customerId,
            CustomerName = customerName,
            CustomerPhone = customerPhone,
            FulfillmentType = fulfillment,
            DeliveryAddress = fulfillment == FulfillmentType.Delivery ? address : null,
            Status = initialStatus,
            Subtotal = price.Subtotal,
            DiscountAmount = price.Discount,
            DeliveryFee = price.DeliveryFee,
            TaxAmount = price.Tax,
            Total = price.Total,
            CouponId = coupon?.Id,
            CouponCode = coupon?.Code,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            IdempotencyKey = idempotencyKey,
            PlacedAt = now,
            Payment = new Payment(paymentMethod, price.Total)
        };

        foreach (var line in lines)
            order._items.Add(new OrderItem(line.ProductId, line.ProductName, line.UnitPrice, line.Quantity));

        order._statusHistory.Add(new OrderStatusChange(null, initialStatus, customerId, "Order created", now));
        if (initialStatus == OrderStatus.Placed) order.Raise(new OrderPlacedEvent(order, now));
        return order;
    }

    public bool IsActive => !OrderStateMachine.IsTerminal(Status);

    public IReadOnlyList<OrderStatus> NextStatusesFor(OrderActor actor) =>
        OrderStateMachine.NextStatuses(Status, actor, FulfillmentType);

    /// <summary>Moves the order to a new status if the transition is legal for the given actor.</summary>
    public void ChangeStatus(OrderStatus to, OrderActor actor, int? actorUserId, string? note, DateTime now)
    {
        if (!OrderStateMachine.CanTransition(Status, to, actor, FulfillmentType))
            throw new DomainException("order.invalid_transition",
                $"Order {OrderNumber} cannot move from {Status} to {to} as {actor}.");

        var from = Status;
        Status = to;
        _statusHistory.Add(new OrderStatusChange(from, to, actorUserId, string.IsNullOrWhiteSpace(note) ? null : note.Trim(), now));

        switch (to)
        {
            case OrderStatus.Cancelled:
                CancellationReason = string.IsNullOrWhiteSpace(note) ? "Cancelled" : note.Trim();
                if (Payment.Status == PaymentStatus.Paid) Payment.MarkRefundPending();
                break;
            case OrderStatus.Delivered or OrderStatus.PickedUp:
                CompletedAt = now;
                // Cash is collected at handover.
                if (Payment.Method == PaymentMethod.CashOnDelivery) Payment.MarkPaid($"COD-{OrderNumber}", now);
                break;
        }

        Raise(new OrderStatusChangedEvent(this, from, to, now));
    }

    public void ConfirmOnlinePayment(string providerReference, DateTime now)
    {
        EnsureAwaitingOnlinePayment();
        Payment.MarkPaid(providerReference, now);
        ChangeStatus(OrderStatus.Placed, OrderActor.System, null, "Payment received", now);
        Raise(new OrderPlacedEvent(this, now));
    }

    public void FailOnlinePayment(string reason, DateTime now)
    {
        EnsureAwaitingOnlinePayment();
        Payment.MarkFailed(reason);
        ChangeStatus(OrderStatus.PaymentFailed, OrderActor.System, null, reason, now);
    }

    public void MarkRefunded(DateTime now)
    {
        if (Payment.Status != PaymentStatus.RefundPending)
            throw new DomainException("payment.not_refundable", "There is no pending refund for this order.");
        Payment.MarkRefunded(now);
    }

    private void EnsureAwaitingOnlinePayment()
    {
        if (Payment.Method != PaymentMethod.OnlineSimulated || Status != OrderStatus.AwaitingPayment)
            throw new DomainException("payment.not_awaiting", "This order is not awaiting online payment.");
    }
}

public class OrderItem : Entity
{
    private OrderItem() { }

    internal OrderItem(int productId, string productName, decimal unitPrice, int quantity)
    {
        if (quantity <= 0) throw new DomainException("order.invalid_quantity", "Quantity must be at least 1.");
        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
        LineTotal = Money.Round(unitPrice * quantity);
    }

    public int OrderId { get; private set; }
    public int ProductId { get; private set; }
    /// <summary>Name and price are snapshots: later menu changes must not rewrite order history.</summary>
    public string ProductName { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public decimal LineTotal { get; private set; }
}

public class OrderStatusChange : Entity
{
    private OrderStatusChange() { }

    internal OrderStatusChange(OrderStatus? from, OrderStatus to, int? changedByUserId, string? note, DateTime at)
    {
        FromStatus = from;
        ToStatus = to;
        ChangedByUserId = changedByUserId;
        Note = note;
        ChangedAt = at;
    }

    public int OrderId { get; private set; }
    public OrderStatus? FromStatus { get; private set; }
    public OrderStatus ToStatus { get; private set; }
    public int? ChangedByUserId { get; private set; }
    public string? Note { get; private set; }
    public DateTime ChangedAt { get; private set; }
}

public class Payment : Entity
{
    private Payment() { }

    internal Payment(PaymentMethod method, decimal amount)
    {
        Method = method;
        Amount = amount;
        Status = PaymentStatus.Pending;
    }

    public int OrderId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public PaymentStatus Status { get; private set; }
    public decimal Amount { get; private set; }
    /// <summary>Gateway transaction id. Card details are never received or stored by BiteBox.</summary>
    public string? ProviderReference { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime? PaidAt { get; private set; }
    public DateTime? RefundedAt { get; private set; }

    internal void MarkPaid(string reference, DateTime now)
    {
        Status = PaymentStatus.Paid;
        ProviderReference = reference;
        PaidAt = now;
        FailureReason = null;
    }

    internal void MarkFailed(string reason)
    {
        Status = PaymentStatus.Failed;
        FailureReason = reason;
    }

    internal void MarkRefundPending() => Status = PaymentStatus.RefundPending;

    internal void MarkRefunded(DateTime now)
    {
        Status = PaymentStatus.Refunded;
        RefundedAt = now;
    }
}

/// <summary>Value object: an immutable copy of where the order was delivered.</summary>
public sealed record DeliveryAddress(
    string RecipientName, string Phone, string Line1, string? Line2, string City, string Pincode, string? Landmark);
