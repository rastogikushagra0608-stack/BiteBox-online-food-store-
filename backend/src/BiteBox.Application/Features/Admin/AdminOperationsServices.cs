using System.Globalization;
using System.Text;
using BiteBox.Application.Abstractions;
using BiteBox.Application.Common;
using BiteBox.Application.Features.Orders;
using BiteBox.Domain.Enums;
using BiteBox.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BiteBox.Application.Features.Admin;

/// <summary>Order operations for staff. What each role may do is decided by the order state machine.</summary>
public sealed class AdminOrderService(IApplicationDbContext db, ICurrentUser currentUser, StockLedger stockLedger, TimeProvider clock)
{
    public async Task<PagedResult<OrderSummaryDto>> ListAsync(AdminOrderQuery q, CancellationToken ct)
    {
        var (page, size) = Paging.Normalize(q.Page, q.PageSize, 20);
        var query = db.Orders.AsNoTracking().AsQueryable();

        // Delivery partners only see delivery orders that are ready or on the road.
        if (Roles.ToOrderActor(currentUser) == OrderActor.Delivery)
            query = query.Where(o => o.FulfillmentType == FulfillmentType.Delivery &&
                                     (o.Status == OrderStatus.Ready || o.Status == OrderStatus.OutForDelivery || o.Status == OrderStatus.Delivered));

        if (q.Status is not null) query = query.Where(o => o.Status == q.Status);
        if (q.ActiveOnly) query = query.Where(o => OrderStateMachine.ActiveStatuses.Contains(o.Status));
        if (q.Fulfillment is not null) query = query.Where(o => o.FulfillmentType == q.Fulfillment);
        if (q.From is not null) query = query.Where(o => o.PlacedAt >= q.From);
        if (q.To is not null) query = query.Where(o => o.PlacedAt < q.To);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var pattern = $"%{q.Search.Trim()}%";
            query = query.Where(o => EF.Functions.Like(o.OrderNumber, pattern) || EF.Functions.Like(o.CustomerName, pattern) || EF.Functions.Like(o.CustomerPhone, pattern));
        }

        var total = await query.CountAsync(ct);
        var ordered = q.ActiveOnly ? query.OrderBy(o => o.PlacedAt) : query.OrderByDescending(o => o.PlacedAt); // kitchen queue = oldest first
        var items = await OrderQueries.ToSummariesAsync(db, ordered.Skip((page - 1) * size).Take(size), ct);
        return new PagedResult<OrderSummaryDto>(items, page, size, total);
    }

    public async Task<OrderDetailDto> GetAsync(string orderNumber, CancellationToken ct)
    {
        var order = await OrderQueries.LoadAggregate(db).AsNoTracking().FirstOrDefaultAsync(o => o.OrderNumber == orderNumber, ct)
                    ?? throw new NotFoundException("Order", orderNumber);
        var info = await OrderQueries.ProductInfoAsync(db, order, ct);
        return OrderMapping.ToDetail(order, Roles.ToOrderActor(currentUser), info);
    }

    public async Task<OrderDetailDto> ChangeStatusAsync(string orderNumber, ChangeOrderStatusRequest r, CancellationToken ct)
    {
        var actor = Roles.ToOrderActor(currentUser);
        var userId = currentUser.RequireUserId();
        var order = await OrderQueries.LoadAggregate(db).FirstOrDefaultAsync(o => o.OrderNumber == orderNumber, ct)
                    ?? throw new NotFoundException("Order", orderNumber);

        if (!OrderStateMachine.CanTransition(order.Status, r.Status, actor, order.FulfillmentType))
            throw new ForbiddenException($"As {actor} you cannot move this order from {order.Status} to {r.Status}.");

        await db.InTransactionAsync(async token =>
        {
            order.ChangeStatus(r.Status, actor, userId, r.Note, clock.GetUtcNow().UtcDateTime);
            if (r.Status == OrderStatus.Cancelled) await stockLedger.ReleaseOrderAsync(order, userId, token);
            await db.SaveChangesAsync(token);
            return true;
        }, ct);

        return await GetAsync(orderNumber, ct);
    }

    public async Task<OrderDetailDto> MarkRefundedAsync(string orderNumber, CancellationToken ct)
    {
        var order = await OrderQueries.LoadAggregate(db).FirstOrDefaultAsync(o => o.OrderNumber == orderNumber, ct)
                    ?? throw new NotFoundException("Order", orderNumber);
        order.MarkRefunded(clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return await GetAsync(orderNumber, ct);
    }
}

public sealed class AdminUserService(IApplicationDbContext db, IIdentityService identity, ICurrentUser currentUser)
{
    public async Task<PagedResult<AdminUserDto>> ListAsync(string? search, string? role, int page, int pageSize, CancellationToken ct)
    {
        var users = await identity.ListUsersAsync(search, role, page, pageSize, ct);
        var ids = users.Items.Select(u => u.Id).ToList();
        var stats = await db.Orders.AsNoTracking()
            .Where(o => ids.Contains(o.CustomerId) && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.PaymentFailed && o.Status != OrderStatus.AwaitingPayment)
            .GroupBy(o => o.CustomerId)
            .Select(g => new { CustomerId = g.Key, Count = g.Count(), Spent = g.Sum(o => o.Total) })
            .ToDictionaryAsync(x => x.CustomerId, ct);

        return new PagedResult<AdminUserDto>(users.Items.Select(u =>
        {
            stats.TryGetValue(u.Id, out var s);
            return new AdminUserDto(u.Id, u.FullName, u.Email, u.PhoneNumber, u.IsActive, u.Roles, u.CreatedAt, s?.Count ?? 0, s?.Spent ?? 0);
        }).ToList(), users.Page, users.PageSize, users.TotalCount);
    }

    public async Task<PagedResult<OrderSummaryDto>> OrdersOfAsync(int userId, CancellationToken ct)
    {
        var query = db.Orders.AsNoTracking().Where(o => o.CustomerId == userId).OrderByDescending(o => o.PlacedAt);
        var items = await OrderQueries.ToSummariesAsync(db, query.Take(50), ct);
        return new PagedResult<OrderSummaryDto>(items, 1, 50, items.Count);
    }

    public Task SetActiveAsync(int userId, bool isActive, CancellationToken ct)
    {
        if (userId == currentUser.UserId && !isActive) throw new BadRequestException("user.self", "You cannot deactivate your own account.");
        return identity.SetActiveAsync(userId, isActive, ct);
    }

    public Task SetRoleAsync(int userId, string role, CancellationToken ct)
    {
        if (!Roles.All.Contains(role)) throw new BadRequestException("user.role", "Unknown role.");
        if (userId == currentUser.UserId) throw new BadRequestException("user.self", "You cannot change your own role.");
        return identity.SetRoleAsync(userId, role, ct);
    }

    public Task<UserSummary> CreateStaffAsync(CreateStaffRequest r, CancellationToken ct) =>
        identity.CreateStaffAsync(r.FullName.Trim(), r.Email.Trim(), r.Phone.Trim(), r.Password, r.Role, ct);
}

/// <summary>Restaurant-level settings (bound from "BiteBox:Restaurant").</summary>
public sealed class RestaurantOptions
{
    public const string Section = "BiteBox:Restaurant";
    public string Name { get; set; } = "BiteBox";
    /// <summary>Offset used to bucket sales into local business days. India = +330 minutes.</summary>
    public int UtcOffsetMinutes { get; set; } = 330;
}

/// <summary>
/// Dashboard and reports. Revenue counts orders that were actually placed (excludes awaiting payment,
/// failed payments and cancellations). Date ranges are capped, so daily bucketing is done in memory
/// in local business time, which keeps SQL simple and timezone-correct.
/// </summary>
public sealed class ReportService(IApplicationDbContext db, IIdentityService identity, IOptions<RestaurantOptions> restaurant, TimeProvider clock)
{
    public const int MaxRangeDays = 366;
    private TimeSpan Offset => TimeSpan.FromMinutes(restaurant.Value.UtcOffsetMinutes);

    private static readonly OrderStatus[] NotRevenue = [OrderStatus.AwaitingPayment, OrderStatus.PaymentFailed, OrderStatus.Cancelled];

    public async Task<DashboardDto> DashboardAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var (fromDate, toDate, fromUtc, toUtc) = Range(from, to);

        var orders = await db.Orders.AsNoTracking().Where(o => o.PlacedAt >= fromUtc && o.PlacedAt < toUtc)
            .Select(o => new { o.Id, o.Status, o.Total, o.PlacedAt }).ToListAsync(ct);
        var revenueOrders = orders.Where(o => !NotRevenue.Contains(o.Status)).ToList();

        var byDay = Enumerable.Range(0, toDate.DayNumber - fromDate.DayNumber + 1)
            .Select(i => fromDate.AddDays(i))
            .GroupJoin(revenueOrders, d => d, o => LocalDate(o.PlacedAt), (d, os) =>
            {
                var list = os.ToList();
                return new DailySalesDto(d, list.Count, list.Sum(x => x.Total));
            }).ToList();

        var statusBreakdown = orders.GroupBy(o => o.Status).Select(g => new StatusCountDto(g.Key, g.Count())).OrderBy(s => s.Status).ToList();
        var top = await TopProductsAsync(fromUtc, toUtc, 5, ct);

        var lowStock = await db.Products.AsNoTracking().Where(p => !p.IsDeleted && p.StockQuantity <= p.LowStockThreshold)
            .OrderBy(p => p.StockQuantity).Take(8)
            .Select(p => new InventoryItemDto(p.Id, p.Name, p.Category!.Name, p.ImageUrl, p.StockQuantity, p.LowStockThreshold, true, p.IsAvailable))
            .ToListAsync(ct);

        var recent = await OrderQueries.ToSummariesAsync(db, db.Orders.AsNoTracking().OrderByDescending(o => o.PlacedAt).Take(6), ct);
        var revenue = revenueOrders.Sum(o => o.Total);

        // Active orders are "right now", independent of the selected range.
        var activeNow = await db.Orders.CountAsync(o => OrderStateMachine.ActiveStatuses.Contains(o.Status), ct);

        return new DashboardDto(fromDate, toDate, revenueOrders.Count, revenue,
            revenueOrders.Count == 0 ? 0 : Math.Round(revenue / revenueOrders.Count, 2),
            activeNow,
            orders.Count(o => o.Status is OrderStatus.Delivered or OrderStatus.PickedUp),
            orders.Count(o => o.Status == OrderStatus.Cancelled),
            await identity.CountCustomersAsync(fromUtc, ct),
            byDay, statusBreakdown, top, lowStock, recent);
    }

    public async Task<IReadOnlyList<TopProductDto>> TopProductsAsync(DateTime fromUtc, DateTime toUtc, int take, CancellationToken ct)
    {
        var rows = await db.OrderItems.AsNoTracking()
            .Where(i => db.Orders.Any(o => o.Id == i.OrderId && o.PlacedAt >= fromUtc && o.PlacedAt < toUtc && !NotRevenue.Contains(o.Status)))
            .GroupBy(i => new { i.ProductId, i.ProductName })
            .Select(g => new { g.Key.ProductId, g.Key.ProductName, Qty = g.Sum(i => i.Quantity), Revenue = g.Sum(i => i.LineTotal) })
            .OrderByDescending(x => x.Qty).Take(take).ToListAsync(ct);

        var ids = rows.Select(r => r.ProductId).ToList();
        var images = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.ImageUrl, ct);
        return rows.Select(r => new TopProductDto(r.ProductId, r.ProductName, images.GetValueOrDefault(r.ProductId), r.Qty, r.Revenue)).ToList();
    }

    public async Task<IReadOnlyList<DailySalesDto>> SalesAsync(DateOnly? from, DateOnly? to, CancellationToken ct) =>
        (await DashboardAsync(from, to, ct)).SalesByDay;

    public async Task<IReadOnlyList<TopProductDto>> ProductPerformanceAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var (_, _, fromUtc, toUtc) = Range(from, to);
        return await TopProductsAsync(fromUtc, toUtc, 100, ct);
    }

    public async Task<string> SalesCsvAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var rows = await SalesAsync(from, to, ct);
        var sb = new StringBuilder("Date,Orders,Revenue (INR)\n");
        foreach (var r in rows) sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{r.Date:yyyy-MM-dd},{r.Orders},{r.Revenue:0.00}"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Total,{rows.Sum(r => r.Orders)},{rows.Sum(r => r.Revenue):0.00}"));
        return sb.ToString();
    }

    public async Task<string> ProductsCsvAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var rows = await ProductPerformanceAsync(from, to, ct);
        var sb = new StringBuilder("Product,Quantity sold,Revenue (INR)\n");
        foreach (var r in rows) sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"\"{r.Name.Replace("\"", "\"\"")}\",{r.QuantitySold},{r.Revenue:0.00}"));
        return sb.ToString();
    }

    private (DateOnly From, DateOnly To, DateTime FromUtc, DateTime ToUtc) Range(DateOnly? from, DateOnly? to)
    {
        var today = LocalDate(clock.GetUtcNow().UtcDateTime);
        var toDate = to ?? today;
        var fromDate = from ?? toDate.AddDays(-29);
        if (fromDate > toDate) throw new BadRequestException("report.range", "'from' must be on or before 'to'.");
        if (toDate.DayNumber - fromDate.DayNumber >= MaxRangeDays) throw new BadRequestException("report.range", $"Date range cannot exceed {MaxRangeDays} days.");

        var fromUtc = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) - Offset;
        var toUtc = toDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) - Offset;
        return (fromDate, toDate, fromUtc, toUtc);
    }

    private DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(utc + Offset);
}
