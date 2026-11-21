using System.Security.Cryptography;
using BiteBox.Application.Abstractions;
using BiteBox.Application.Common;
using BiteBox.Domain.Entities;
using BiteBox.Domain.Enums;
using BiteBox.Domain.Exceptions;
using BiteBox.Domain.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BiteBox.Application.Features.Orders;

/// <summary>
/// Checkout and customer order use cases.
///
/// Key rules enforced here:
///  • Prices are always re-read from the database; the client never sends prices.
///  • Stock is decremented with a single conditional UPDATE per line (WHERE stock >= qty) inside a
///    transaction, so two customers racing for the last portion cannot both succeed.
///  • A client-generated idempotency key makes "double-clicked Place Order" return the same order.
/// </summary>
public sealed class OrderService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IIdentityService identity,
    FulfillmentFeeCalculator feeCalculator,
    StockLedger stockLedger,
    IOptions<PricingOptions> pricingOptions,
    TimeProvider clock,
    ILogger<OrderService> logger)
{
    private PricingOptions Pricing => pricingOptions.Value;

    public async Task<QuoteDto> QuoteAsync(QuoteRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var cart = await LoadCartAsync(userId, ct);
        var problems = new List<string>();

        if (cart.Count == 0) problems.Add("Your cart is empty.");
        problems.AddRange(StockProblems(cart));

        var lines = ToPriceLines(cart);
        var subtotal = lines.Sum(l => l.LineTotal);

        decimal discount = 0;
        string? couponMessage = null;
        string? appliedCode = null;
        var couponValid = false;
        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            try
            {
                var (coupon, d) = await ResolveCouponAsync(request.CouponCode, userId, subtotal, ct);
                discount = d;
                appliedCode = coupon.Code;
                couponValid = true;
                couponMessage = $"{coupon.Code} applied: {coupon.Description}";
            }
            catch (Exception ex) when (ex is DomainException or NotFoundException)
            {
                couponMessage = ex is NotFoundException ? "This coupon code is not valid." : ex.Message;
            }
        }

        if (cart.Count > 0 && subtotal < Pricing.MinimumOrderAmount)
            problems.Add($"Minimum order amount is ₹{Pricing.MinimumOrderAmount:0}.");

        var fee = feeCalculator.Calculate(request.FulfillmentType, subtotal - discount);
        var price = PriceCalculator.Calculate(lines, discount, fee, Pricing.TaxRate);

        return new QuoteDto(
            cart.Select(c => new QuoteLineDto(c.ProductId, c.Product!.Name, c.Product.ImageUrl, c.Product.Price, c.Quantity, c.Product.Price * c.Quantity)).ToList(),
            price.Subtotal, price.Discount, price.DeliveryFee, price.Tax, price.Total,
            appliedCode, couponMessage, couponValid, Pricing.MinimumOrderAmount, Pricing.FreeDeliveryThreshold, Pricing.TaxRate, problems);
    }

    public async Task<OrderDetailDto> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();

        // 1. Idempotency: a retry of the same checkout returns the original order.
        var existing = await FindByIdempotencyKeyAsync(userId, request.IdempotencyKey, ct);
        if (existing is { } existingId) return await GetAsync(existingId, ct);

        var user = await identity.GetUserAsync(userId, ct) ?? throw new UnauthorizedException();

        // 2. Load cart with *current* prices.
        var cart = await LoadCartAsync(userId, ct);
        if (cart.Count == 0) throw new BadRequestException("order.empty_cart", "Your cart is empty.");
        var stockProblems = StockProblems(cart).ToList();
        if (stockProblems.Count > 0) throw new ConflictException("order.stock", string.Join(" ", stockProblems));

        // 3. Address snapshot (must belong to this customer).
        DeliveryAddress? address = null;
        if (request.FulfillmentType == FulfillmentType.Delivery)
        {
            var saved = await db.Addresses.AsNoTracking().FirstOrDefaultAsync(a => a.Id == request.AddressId && a.UserId == userId, ct)
                        ?? throw new BadRequestException("order.address", "Please choose a valid delivery address.");
            address = new DeliveryAddress(saved.RecipientName, saved.Phone, saved.Line1, saved.Line2, saved.City, saved.Pincode, saved.Landmark);
        }

        // 4. Server-side pricing.
        var lines = ToPriceLines(cart);
        var subtotal = lines.Sum(l => l.LineTotal);
        if (subtotal < Pricing.MinimumOrderAmount)
            throw new BadRequestException("order.minimum", $"Minimum order amount is ₹{Pricing.MinimumOrderAmount:0}.");

        Coupon? coupon = null;
        decimal discount = 0;
        if (!string.IsNullOrWhiteSpace(request.CouponCode))
            (coupon, discount) = await ResolveCouponAsync(request.CouponCode, userId, subtotal, ct);

        var fee = feeCalculator.Calculate(request.FulfillmentType, subtotal - discount);
        var price = PriceCalculator.Calculate(lines, discount, fee, Pricing.TaxRate);
        var now = clock.GetUtcNow().UtcDateTime;

        try
        {
            var orderId = await db.InTransactionAsync(async token =>
            {
                // 5. Reserve stock atomically. Any failure throws and rolls back every reservation.
                foreach (var line in lines)
                {
                    if (!await stockLedger.TryReserveAsync(line.ProductId, line.Quantity, token))
                        throw new ConflictException("order.stock", $"Sorry, {line.ProductName} just sold out or has fewer portions left than you requested.");
                }

                if (coupon is not null && !await TryConsumeCouponAsync(coupon.Id, token))
                    throw new ConflictException("coupon.exhausted", "This coupon has just reached its usage limit.");

                var order = Order.Create(GenerateOrderNumber(now), userId, user.FullName, user.PhoneNumber ?? address?.Phone ?? "",
                    request.FulfillmentType, address, request.PaymentMethod, lines, price, coupon, request.Notes,
                    request.IdempotencyKey, now);
                db.Orders.Add(order);
                await db.SaveChangesAsync(token);

                await stockLedger.RecordAsync(lines.Select(l => (l.ProductId, -l.Quantity)), InventoryReason.OrderPlaced, order.Id, userId, $"Order {order.OrderNumber}", token);
                await db.CartItems.Where(c => c.UserId == userId).ExecuteDeleteAsync(token);
                await db.SaveChangesAsync(token);
                return order.Id;
            }, ct);

            logger.LogInformation("Order {OrderId} placed by user {UserId} for {Total}", orderId, userId, price.Total);
            return await GetAsync(orderId, ct);
        }
        catch (DbUpdateException)
        {
            // Two concurrent requests with the same idempotency key: the unique index rejects the second.
            var winner = await FindByIdempotencyKeyAsync(userId, request.IdempotencyKey, ct);
            if (winner is { } winnerId) return await GetAsync(winnerId, ct);
            throw;
        }
    }

    public async Task<PagedResult<OrderSummaryDto>> ListMineAsync(int page, int pageSize, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var (p, size) = Paging.Normalize(page, pageSize, 10);
        var query = db.Orders.AsNoTracking().Where(o => o.CustomerId == userId);
        var total = await query.CountAsync(ct);
        var items = await OrderQueries.ToSummariesAsync(db, query.OrderByDescending(o => o.PlacedAt).Skip((p - 1) * size).Take(size), ct);
        return new PagedResult<OrderSummaryDto>(items, p, size, total);
    }

    public async Task<OrderDetailDto> GetMineAsync(string orderNumber, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var id = await db.Orders.Where(o => o.OrderNumber == orderNumber && o.CustomerId == userId).Select(o => (int?)o.Id).FirstOrDefaultAsync(ct)
                 ?? throw new NotFoundException("Order", orderNumber); // 404 (not 403) so order numbers can't be probed
        return await GetAsync(id, ct);
    }

    public async Task<OrderDetailDto> CancelMineAsync(string orderNumber, string? reason, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var order = await OrderQueries.LoadAggregate(db).FirstOrDefaultAsync(o => o.OrderNumber == orderNumber && o.CustomerId == userId, ct)
                    ?? throw new NotFoundException("Order", orderNumber);

        if (!order.NextStatusesFor(OrderActor.Customer).Contains(OrderStatus.Cancelled))
            throw new ConflictException("order.cannot_cancel", "This order is already being prepared and can no longer be cancelled. Please contact the restaurant.");

        await db.InTransactionAsync(async token =>
        {
            order.ChangeStatus(OrderStatus.Cancelled, OrderActor.Customer, userId, reason ?? "Cancelled by customer", clock.GetUtcNow().UtcDateTime);
            await stockLedger.ReleaseOrderAsync(order, userId, token);
            await db.SaveChangesAsync(token);
            return true;
        }, ct);

        return await GetAsync(order.Id, ct);
    }

    /// <summary>Adds the still-available items of a past order back into the cart.</summary>
    public async Task<int> ReorderAsync(string orderNumber, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var items = await db.Orders.AsNoTracking().Where(o => o.OrderNumber == orderNumber && o.CustomerId == userId)
            .SelectMany(o => o.Items).Select(i => new { i.ProductId, i.Quantity }).ToListAsync(ct);
        if (items.Count == 0) throw new NotFoundException("Order", orderNumber);

        var ids = items.Select(i => i.ProductId).ToList();
        var products = await db.Products.Where(p => ids.Contains(p.Id) && p.IsAvailable && !p.IsDeleted && p.StockQuantity > 0).ToDictionaryAsync(p => p.Id, ct);
        var cart = await db.CartItems.Where(c => c.UserId == userId).ToDictionaryAsync(c => c.ProductId, ct);
        var added = 0;

        foreach (var item in items)
        {
            if (!products.TryGetValue(item.ProductId, out var product)) continue;
            var qty = Math.Min(Math.Min(item.Quantity, product.StockQuantity), CartItem.MaxQuantityPerItem);
            if (cart.TryGetValue(product.Id, out var line)) line.Quantity = Math.Max(line.Quantity, qty);
            else db.CartItems.Add(new CartItem { UserId = userId, ProductId = product.Id, Quantity = qty, AddedAt = clock.GetUtcNow().UtcDateTime });
            added++;
        }

        await db.SaveChangesAsync(ct);
        return added;
    }

    internal async Task<OrderDetailDto> GetAsync(int orderId, CancellationToken ct)
    {
        var order = await OrderQueries.LoadAggregate(db).AsNoTracking().FirstAsync(o => o.Id == orderId, ct);
        var info = await OrderQueries.ProductInfoAsync(db, order, ct);
        return OrderMapping.ToDetail(order, OrderActor.Customer, info);
    }

    private Task<int?> FindByIdempotencyKeyAsync(int userId, string key, CancellationToken ct) =>
        db.Orders.Where(o => o.CustomerId == userId && o.IdempotencyKey == key).Select(o => (int?)o.Id).FirstOrDefaultAsync(ct);

    private Task<List<CartItem>> LoadCartAsync(int userId, CancellationToken ct) =>
        db.CartItems.AsNoTracking().Include(c => c.Product).Where(c => c.UserId == userId).OrderBy(c => c.AddedAt).ToListAsync(ct);

    private static List<PriceLine> ToPriceLines(IEnumerable<CartItem> cart) =>
        cart.Select(c => new PriceLine(c.ProductId, c.Product!.Name, c.Product.Price, c.Quantity)).ToList();

    private static IEnumerable<string> StockProblems(IEnumerable<CartItem> cart)
    {
        foreach (var c in cart)
        {
            var p = c.Product!;
            if (!p.CanBeOrdered) yield return $"{p.Name} is currently unavailable.";
            else if (p.StockQuantity < c.Quantity) yield return $"Only {p.StockQuantity} portions of {p.Name} are left.";
        }
    }

    private async Task<(Coupon Coupon, decimal Discount)> ResolveCouponAsync(string code, int userId, decimal subtotal, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var coupon = await db.Coupons.AsNoTracking().FirstOrDefaultAsync(c => c.Code == normalized, ct)
                     ?? throw new NotFoundException("Coupon", normalized);
        var timesUsed = await db.Orders.CountAsync(o => o.CustomerId == userId && o.CouponId == coupon.Id
                                                        && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.PaymentFailed, ct);
        return (coupon, coupon.CalculateDiscountFor(subtotal, clock.GetUtcNow().UtcDateTime, timesUsed));
    }

    private async Task<bool> TryConsumeCouponAsync(int couponId, CancellationToken ct) =>
        await db.Coupons
            .Where(c => c.Id == couponId && (c.UsageLimit == null || c.UsedCount < c.UsageLimit))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedCount, c => c.UsedCount + 1), ct) == 1;

    /// <summary>e.g. BB-261120-K7Q2X9 (date + 6 random base-32 characters).</summary>
    private static string GenerateOrderNumber(DateTime now)
    {
        const string alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ"; // no 0/O/1/I to avoid confusion
        Span<char> suffix = stackalloc char[6];
        for (var i = 0; i < suffix.Length; i++) suffix[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return $"BB-{now:yyMMdd}-{new string(suffix)}";
    }
}

internal static class OrderQueries
{
    public static IQueryable<Order> LoadAggregate(IApplicationDbContext db) =>
        db.Orders.Include(o => o.Items).Include(o => o.StatusHistory).Include(o => o.Payment); // split-query behaviour is configured globally in Infrastructure

    /// <summary>
    /// Projects orders to summaries in two simple queries (orders, then item names) instead of one
    /// complex nested projection, which keeps the generated SQL portable across MySQL providers.
    /// </summary>
    public static async Task<List<OrderSummaryDto>> ToSummariesAsync(IApplicationDbContext db, IQueryable<Order> orders, CancellationToken ct)
    {
        var rows = await orders.Select(o => new
        {
            o.Id, o.OrderNumber, o.Status, o.FulfillmentType, o.Payment.Method, PaymentStatus = o.Payment.Status,
            o.Total, o.PlacedAt, o.CustomerName
        }).ToListAsync(ct);

        var ids = rows.Select(r => r.Id).ToList();
        var items = await db.OrderItems.AsNoTracking().Where(i => ids.Contains(i.OrderId))
            .Select(i => new { i.OrderId, i.Id, i.ProductName, i.Quantity }).ToListAsync(ct);
        var byOrder = items.GroupBy(i => i.OrderId).ToDictionary(g => g.Key, g => g.OrderBy(i => i.Id).ToList());

        return rows.Select(r =>
        {
            var lines = byOrder.GetValueOrDefault(r.Id) ?? [];
            return new OrderSummaryDto(r.Id, r.OrderNumber, r.Status, r.FulfillmentType, r.Method, r.PaymentStatus, r.Total,
                lines.Sum(l => l.Quantity), string.Join(", ", lines.Take(3).Select(l => l.ProductName)) + (lines.Count > 3 ? "…" : ""),
                r.PlacedAt, r.CustomerName);
        }).ToList();
    }

    public static async Task<IReadOnlyDictionary<int, (string? Image, string Slug)>> ProductInfoAsync(IApplicationDbContext db, Order order, CancellationToken ct)
    {
        var ids = order.Items.Select(i => i.ProductId).Distinct().ToList();
        return await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => (p.ImageUrl, p.Slug), ct);
    }
}
