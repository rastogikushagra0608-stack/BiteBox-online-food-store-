using BiteBox.Application.Abstractions;
using BiteBox.Application.Common;
using BiteBox.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BiteBox.Application.Features.Orders;

public sealed record SimulatePaymentRequest(bool Success);

/// <summary>
/// Completes online payment for an order that is awaiting payment. The gateway is behind the
/// <see cref="IPaymentGateway"/> adapter; this project ships a simulator so no real money moves.
/// </summary>
public sealed class PaymentService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IPaymentGateway gateway,
    StockLedger stockLedger,
    OrderService orders,
    TimeProvider clock,
    ILogger<PaymentService> logger)
{
    public async Task<OrderDetailDto> PayAsync(string orderNumber, SimulatePaymentRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var order = await OrderQueries.LoadAggregate(db).FirstOrDefaultAsync(o => o.OrderNumber == orderNumber && o.CustomerId == userId, ct)
                    ?? throw new NotFoundException("Order", orderNumber);

        if (order.Status != OrderStatus.AwaitingPayment || order.Payment.Method != PaymentMethod.OnlineSimulated)
            throw new ConflictException("payment.not_awaiting", "This order is not awaiting payment.");

        var result = await gateway.ChargeAsync(new PaymentGatewayRequest(order.OrderNumber, order.Total, request.Success), ct);
        var now = clock.GetUtcNow().UtcDateTime;

        await db.InTransactionAsync(async token =>
        {
            if (result.Succeeded)
            {
                order.ConfirmOnlinePayment(result.TransactionReference!, now);
            }
            else
            {
                order.FailOnlinePayment(result.FailureReason ?? "Payment declined", now);
                await stockLedger.ReleaseOrderAsync(order, userId, token);
            }
            await db.SaveChangesAsync(token);
            return true;
        }, ct);

        logger.LogInformation("Payment for order {OrderNumber} via {Gateway}: {Outcome}", order.OrderNumber, gateway.Name,
            result.Succeeded ? "succeeded" : "failed");
        return await orders.GetAsync(order.Id, ct);
    }
}
