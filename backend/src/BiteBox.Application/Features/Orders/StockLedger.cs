using BiteBox.Application.Abstractions;
using BiteBox.Domain.Entities;
using BiteBox.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BiteBox.Application.Features.Orders;

/// <summary>
/// Owns every change to product stock and its audit trail (inventory_transactions).
///
/// Concurrency approach: instead of "read stock → check → write" (which races), reservation is a single
/// <c>UPDATE products SET stock = stock - @qty WHERE id = @id AND stock >= @qty AND available</c>.
/// MySQL row-locks during the UPDATE, so if two orders compete for the last portion exactly one
/// update affects a row; the other sees 0 rows affected and its transaction is rolled back.
/// </summary>
public sealed class StockLedger(IApplicationDbContext db, TimeProvider clock)
{
    public async Task<bool> TryReserveAsync(int productId, int quantity, CancellationToken ct) =>
        await db.Products
            .Where(p => p.Id == productId && !p.IsDeleted && p.IsAvailable && p.StockQuantity >= quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, p => p.StockQuantity - quantity), ct) == 1;

    /// <summary>Manual adjustment (restock/wastage). Fails instead of letting stock go negative.</summary>
    public async Task<bool> TryAdjustAsync(int productId, int delta, CancellationToken ct) =>
        await db.Products
            .Where(p => p.Id == productId && p.StockQuantity + delta >= 0)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, p => p.StockQuantity + delta), ct) == 1;

    /// <summary>Writes audit rows for stock changes that were already applied with ExecuteUpdate.</summary>
    public async Task RecordAsync(IEnumerable<(int ProductId, int Change)> changes, InventoryReason reason, int? orderId, int? userId, string? note, CancellationToken ct)
    {
        var list = changes.ToList();
        var ids = list.Select(c => c.ProductId).ToList();
        var stockNow = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.StockQuantity, ct);
        var now = clock.GetUtcNow().UtcDateTime;

        foreach (var (productId, change) in list)
        {
            db.InventoryTransactions.Add(new InventoryTransaction
            {
                ProductId = productId,
                QuantityChange = change,
                StockAfter = stockNow.GetValueOrDefault(productId),
                Reason = reason,
                OrderId = orderId,
                PerformedByUserId = userId,
                Note = note,
                CreatedAt = now
            });
        }
    }

    /// <summary>Returns an order's portions to stock and frees its coupon redemption (cancel / payment failure).</summary>
    public async Task ReleaseOrderAsync(Order order, int? userId, CancellationToken ct)
    {
        foreach (var item in order.Items)
        {
            var qty = item.Quantity;
            await db.Products.Where(p => p.Id == item.ProductId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, p => p.StockQuantity + qty), ct);
        }

        if (order.CouponId is { } couponId)
        {
            await db.Coupons.Where(c => c.Id == couponId && c.UsedCount > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedCount, c => c.UsedCount - 1), ct);
        }

        await RecordAsync(order.Items.Select(i => (i.ProductId, i.Quantity)), InventoryReason.OrderCancelled, order.Id, userId,
            $"Order {order.OrderNumber} {order.Status}", ct);
    }
}
