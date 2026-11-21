using BiteBox.Domain.Entities;
using BiteBox.Domain.Enums;
using BiteBox.Domain.Events;
using BiteBox.Domain.Exceptions;
using BiteBox.Domain.Orders;
using BiteBox.Domain.Pricing;

namespace BiteBox.Tests.Domain;

public class OrderTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DeliveryAddress Address = new("Priya", "9876500001", "12 MG Road", null, "Bengaluru", "560001", null);

    private static Order NewOrder(PaymentMethod method = PaymentMethod.CashOnDelivery, FulfillmentType fulfillment = FulfillmentType.Delivery)
    {
        var lines = new List<PriceLine> { new(1, "Paneer Butter Masala", 269m, 2), new(2, "Lemon Iced Tea", 99m, 1) };
        var price = PriceCalculator.Calculate(lines, 0, 0, 0.05m);
        return Order.Create("BB-TEST-1", 10, "Priya", "9876500001", fulfillment,
            fulfillment == FulfillmentType.Delivery ? Address : null, method, lines, price, null, null, "key-12345", Now);
    }

    [Fact]
    public void Cash_order_starts_placed_and_raises_order_placed_event()
    {
        var order = NewOrder();

        Assert.Equal(OrderStatus.Placed, order.Status);
        Assert.Equal(PaymentStatus.Pending, order.Payment.Status);
        Assert.Single(order.DomainEvents.OfType<OrderPlacedEvent>());
    }

    [Fact]
    public void Online_order_waits_for_payment_and_does_not_raise_placed_event_yet()
    {
        var order = NewOrder(PaymentMethod.OnlineSimulated);

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Empty(order.DomainEvents.OfType<OrderPlacedEvent>());
    }

    [Fact]
    public void Order_items_snapshot_name_and_price()
    {
        var order = NewOrder();
        var item = order.Items.First();

        Assert.Equal("Paneer Butter Masala", item.ProductName);
        Assert.Equal(269m, item.UnitPrice);
        Assert.Equal(538m, item.LineTotal);
    }

    [Fact]
    public void Delivery_order_requires_address()
    {
        var lines = new List<PriceLine> { new(1, "Dosa", 149m, 1) };
        var ex = Assert.Throws<DomainException>(() => Order.Create("BB-X", 1, "A", "9", FulfillmentType.Delivery, null,
            PaymentMethod.CashOnDelivery, lines, PriceCalculator.Calculate(lines, 0, 40, 0.05m), null, null, "k-1234567", Now));
        Assert.Equal("order.address_required", ex.Code);
    }

    [Fact]
    public void Full_delivery_lifecycle_marks_cash_payment_paid_on_delivery()
    {
        var order = NewOrder();

        order.ChangeStatus(OrderStatus.Confirmed, OrderActor.Kitchen, 2, null, Now.AddMinutes(1));
        order.ChangeStatus(OrderStatus.Preparing, OrderActor.Kitchen, 2, null, Now.AddMinutes(2));
        order.ChangeStatus(OrderStatus.Ready, OrderActor.Kitchen, 2, null, Now.AddMinutes(15));
        order.ChangeStatus(OrderStatus.OutForDelivery, OrderActor.Delivery, 3, null, Now.AddMinutes(17));
        order.ChangeStatus(OrderStatus.Delivered, OrderActor.Delivery, 3, null, Now.AddMinutes(35));

        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal(PaymentStatus.Paid, order.Payment.Status);
        Assert.NotNull(order.CompletedAt);
        Assert.Equal(6, order.StatusHistory.Count); // created + 5 transitions
    }

    [Fact]
    public void Cannot_skip_states()
    {
        var order = NewOrder();
        var ex = Assert.Throws<DomainException>(() => order.ChangeStatus(OrderStatus.Delivered, OrderActor.Admin, 1, null, Now));
        Assert.Equal("order.invalid_transition", ex.Code);
    }

    [Fact]
    public void Customer_cannot_cancel_once_kitchen_confirmed()
    {
        var order = NewOrder();
        order.ChangeStatus(OrderStatus.Confirmed, OrderActor.Kitchen, 2, null, Now);

        Assert.Throws<DomainException>(() => order.ChangeStatus(OrderStatus.Cancelled, OrderActor.Customer, 10, "Changed mind", Now));
        // ...but an admin still can.
        order.ChangeStatus(OrderStatus.Cancelled, OrderActor.Admin, 1, "Out of ingredients", Now);
        Assert.Equal("Out of ingredients", order.CancellationReason);
    }

    [Fact]
    public void Kitchen_cannot_mark_delivered_and_delivery_cannot_confirm()
    {
        Assert.False(OrderStateMachine.CanTransition(OrderStatus.OutForDelivery, OrderStatus.Delivered, OrderActor.Kitchen, FulfillmentType.Delivery));
        Assert.False(OrderStateMachine.CanTransition(OrderStatus.Placed, OrderStatus.Confirmed, OrderActor.Delivery, FulfillmentType.Delivery));
        Assert.False(OrderStateMachine.CanTransition(OrderStatus.Placed, OrderStatus.Confirmed, OrderActor.Customer, FulfillmentType.Delivery));
    }

    [Fact]
    public void Pickup_orders_cannot_go_out_for_delivery()
    {
        Assert.False(OrderStateMachine.CanTransition(OrderStatus.Ready, OrderStatus.OutForDelivery, OrderActor.Admin, FulfillmentType.Pickup));
        Assert.True(OrderStateMachine.CanTransition(OrderStatus.Ready, OrderStatus.PickedUp, OrderActor.Kitchen, FulfillmentType.Pickup));
    }

    [Fact]
    public void Only_system_can_settle_online_payment()
    {
        Assert.False(OrderStateMachine.CanTransition(OrderStatus.AwaitingPayment, OrderStatus.Placed, OrderActor.Admin, FulfillmentType.Delivery));
        Assert.False(OrderStateMachine.CanTransition(OrderStatus.AwaitingPayment, OrderStatus.Placed, OrderActor.Customer, FulfillmentType.Delivery));
    }

    [Fact]
    public void Successful_online_payment_places_order()
    {
        var order = NewOrder(PaymentMethod.OnlineSimulated);
        order.ConfirmOnlinePayment("SIM_123", Now);

        Assert.Equal(OrderStatus.Placed, order.Status);
        Assert.Equal(PaymentStatus.Paid, order.Payment.Status);
        Assert.Equal("SIM_123", order.Payment.ProviderReference);
        Assert.Single(order.DomainEvents.OfType<OrderPlacedEvent>());
    }

    [Fact]
    public void Failed_online_payment_is_terminal()
    {
        var order = NewOrder(PaymentMethod.OnlineSimulated);
        order.FailOnlinePayment("Declined", Now);

        Assert.Equal(OrderStatus.PaymentFailed, order.Status);
        Assert.Equal(PaymentStatus.Failed, order.Payment.Status);
        Assert.Throws<DomainException>(() => order.ConfirmOnlinePayment("SIM_LATE", Now));
    }

    [Fact]
    public void Cancelling_a_paid_order_requires_a_refund()
    {
        var order = NewOrder(PaymentMethod.OnlineSimulated);
        order.ConfirmOnlinePayment("SIM_1", Now);
        order.ChangeStatus(OrderStatus.Cancelled, OrderActor.Customer, 10, "Ordered by mistake", Now);

        Assert.Equal(PaymentStatus.RefundPending, order.Payment.Status);
        order.MarkRefunded(Now.AddHours(1));
        Assert.Equal(PaymentStatus.Refunded, order.Payment.Status);
    }

    [Fact]
    public void Cash_order_cannot_be_refunded()
    {
        var order = NewOrder();
        Assert.Throws<DomainException>(() => order.MarkRefunded(Now));
    }

    [Theory]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.PickedUp)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.PaymentFailed)]
    public void Terminal_states_have_no_next_status(OrderStatus status)
    {
        Assert.True(OrderStateMachine.IsTerminal(status));
        foreach (var actor in Enum.GetValues<OrderActor>())
            Assert.Empty(OrderStateMachine.NextStatuses(status, actor, FulfillmentType.Delivery));
    }
}
