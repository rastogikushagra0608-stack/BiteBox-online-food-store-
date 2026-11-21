namespace BiteBox.Domain.Enums;

public enum OrderStatus
{
    AwaitingPayment = 0,
    Placed = 1,
    Confirmed = 2,
    Preparing = 3,
    Ready = 4,
    OutForDelivery = 5,
    Delivered = 6,
    PickedUp = 7,
    Cancelled = 8,
    PaymentFailed = 9
}

public enum FulfillmentType { Delivery = 0, Pickup = 1 }

public enum PaymentMethod { CashOnDelivery = 0, OnlineSimulated = 1 }

public enum PaymentStatus { Pending = 0, Paid = 1, Failed = 2, RefundPending = 3, Refunded = 4 }

public enum SpiceLevel { None = 0, Mild = 1, Medium = 2, Hot = 3 }

public enum DiscountType { Percentage = 0, Flat = 1 }

public enum InventoryReason { InitialStock = 0, Restock = 1, OrderPlaced = 2, OrderCancelled = 3, ManualAdjustment = 4, Wastage = 5 }

/// <summary>Who is attempting an order transition. Mapped from the authenticated user's role.</summary>
public enum OrderActor { Customer, Kitchen, Delivery, Admin, System }
