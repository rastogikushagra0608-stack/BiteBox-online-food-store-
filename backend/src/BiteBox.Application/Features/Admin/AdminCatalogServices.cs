using BiteBox.Application.Abstractions;
using BiteBox.Application.Common;
using BiteBox.Application.Features.Orders;
using BiteBox.Domain.Entities;
using BiteBox.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BiteBox.Application.Features.Admin;

public sealed class AdminProductService(IApplicationDbContext db, StockLedger stockLedger, ICurrentUser currentUser)
{
    public async Task<PagedResult<AdminProductDto>> ListAsync(AdminProductQuery q, CancellationToken ct)
    {
        var (page, size) = Paging.Normalize(q.Page, q.PageSize, 20);
        var query = db.Products.AsNoTracking().Include(p => p.Category).AsQueryable();
        if (!q.IncludeDeleted) query = query.Where(p => !p.IsDeleted);
        if (q.CategoryId is not null) query = query.Where(p => p.CategoryId == q.CategoryId);
        if (q.LowStockOnly == true) query = query.Where(p => p.StockQuantity <= p.LowStockThreshold);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var pattern = $"%{q.Search.Trim()}%";
            query = query.Where(p => EF.Functions.Like(p.Name, pattern) || EF.Functions.Like(p.Tags, pattern));
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(p => p.Category!.DisplayOrder).ThenBy(p => p.Name).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<AdminProductDto>(rows.Select(ToDto).ToList(), page, size, total);
    }

    public async Task<AdminProductDto> GetAsync(int id, CancellationToken ct) =>
        ToDto(await db.Products.AsNoTracking().Include(p => p.Category).FirstOrDefaultAsync(p => p.Id == id, ct)
              ?? throw new NotFoundException("Product", id));

    public async Task<AdminProductDto> CreateAsync(ProductUpsertRequest r, CancellationToken ct)
    {
        await EnsureCategoryAsync(r.CategoryId, ct);
        var initialStock = r.InitialStock ?? 0;
        var product = new Product(r.CategoryId, r.Name.Trim(), await UniqueSlugAsync(r.Name, null, ct), r.Description.Trim(), r.Price, initialStock);
        Apply(product, r);
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);

        if (initialStock > 0)
        {
            await stockLedger.RecordAsync([(product.Id, initialStock)], InventoryReason.InitialStock, null, currentUser.UserId, "Initial stock", ct);
            await db.SaveChangesAsync(ct);
        }
        return await GetAsync(product.Id, ct);
    }

    public async Task<AdminProductDto> UpdateAsync(int id, ProductUpsertRequest r, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct) ?? throw new NotFoundException("Product", id);
        await EnsureCategoryAsync(r.CategoryId, ct);
        if (!string.Equals(product.Name, r.Name.Trim(), StringComparison.Ordinal))
        {
            product.Name = r.Name.Trim();
            product.Slug = await UniqueSlugAsync(r.Name, id, ct);
        }
        product.CategoryId = r.CategoryId;
        product.Description = r.Description.Trim();
        product.SetPrice(r.Price); // historical orders keep their own price snapshot
        Apply(product, r);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Soft delete keeps order history and reviews intact.</summary>
    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct) ?? throw new NotFoundException("Product", id);
        product.SoftDelete();
        await db.CartItems.Where(c => c.ProductId == id).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<AdminProductDto> SetImageAsync(int id, string imageUrl, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct) ?? throw new NotFoundException("Product", id);
        product.ImageUrl = imageUrl;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private static void Apply(Product p, ProductUpsertRequest r)
    {
        p.ImageUrl = string.IsNullOrWhiteSpace(r.ImageUrl) ? null : r.ImageUrl.Trim();
        p.IsVegetarian = r.IsVegetarian;
        p.SpiceLevel = r.SpiceLevel;
        p.Calories = r.Calories;
        p.PrepTimeMinutes = r.PrepTimeMinutes;
        p.ServesCount = r.ServesCount;
        p.Tags = string.Join(',', (r.Tags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant()).Distinct());
        p.IsAvailable = r.IsAvailable;
        p.IsFeatured = r.IsFeatured;
        p.LowStockThreshold = r.LowStockThreshold;
    }

    private async Task EnsureCategoryAsync(int categoryId, CancellationToken ct)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == categoryId, ct))
            throw new BadRequestException("product.category", "Selected category does not exist.");
    }

    private async Task<string> UniqueSlugAsync(string name, int? excludeId, CancellationToken ct)
    {
        var baseSlug = Slug.From(name);
        var slug = baseSlug;
        for (var i = 2; await db.Products.AnyAsync(p => p.Slug == slug && p.Id != excludeId, ct); i++) slug = $"{baseSlug}-{i}";
        return slug;
    }

    private static AdminProductDto ToDto(Product p) => new(
        p.Id, p.Name, p.Slug, p.Description, p.CategoryId, p.Category?.Name ?? "", p.Price, p.ImageUrl, p.IsVegetarian,
        p.SpiceLevel, p.Calories, p.PrepTimeMinutes, p.ServesCount, p.Tags, p.IsAvailable, p.IsFeatured, p.IsDeleted,
        p.StockQuantity, p.LowStockThreshold, p.IsLowStock, p.AverageRating, p.ReviewCount, p.UpdatedAt);
}

public sealed class AdminCategoryService(IApplicationDbContext db)
{
    public async Task<IReadOnlyList<AdminCategoryDto>> ListAsync(CancellationToken ct) =>
        await db.Categories.AsNoTracking().OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .Select(c => new AdminCategoryDto(c.Id, c.Name, c.Slug, c.Description, c.ImageUrl, c.DisplayOrder, c.IsActive,
                c.Products.Count(p => !p.IsDeleted)))
            .ToListAsync(ct);

    public async Task<AdminCategoryDto> CreateAsync(CategoryUpsertRequest r, CancellationToken ct)
    {
        var slug = Slug.From(r.Name);
        if (await db.Categories.AnyAsync(c => c.Slug == slug, ct))
            throw new ConflictException("category.duplicate", "A category with this name already exists.");
        var category = new Category();
        Apply(category, r, slug);
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).First(c => c.Id == category.Id);
    }

    public async Task<AdminCategoryDto> UpdateAsync(int id, CategoryUpsertRequest r, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Category", id);
        var slug = Slug.From(r.Name);
        if (await db.Categories.AnyAsync(c => c.Slug == slug && c.Id != id, ct))
            throw new ConflictException("category.duplicate", "A category with this name already exists.");
        Apply(category, r, slug);
        await db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).First(c => c.Id == id);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Category", id);
        if (await db.Products.AnyAsync(p => p.CategoryId == id, ct))
            throw new ConflictException("category.in_use", "This category still has dishes. Move them or hide the category instead.");
        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
    }

    private static void Apply(Category c, CategoryUpsertRequest r, string slug)
    {
        c.Name = r.Name.Trim();
        c.Slug = slug;
        c.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        c.ImageUrl = string.IsNullOrWhiteSpace(r.ImageUrl) ? null : r.ImageUrl.Trim();
        c.DisplayOrder = r.DisplayOrder;
        c.IsActive = r.IsActive;
    }
}

public sealed class InventoryService(IApplicationDbContext db, StockLedger stockLedger, ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<InventoryItemDto>> ListAsync(bool lowStockOnly, CancellationToken ct)
    {
        var query = db.Products.AsNoTracking().Where(p => !p.IsDeleted);
        if (lowStockOnly) query = query.Where(p => p.StockQuantity <= p.LowStockThreshold);
        return await query.OrderBy(p => p.StockQuantity - p.LowStockThreshold).ThenBy(p => p.Name)
            .Select(p => new InventoryItemDto(p.Id, p.Name, p.Category!.Name, p.ImageUrl, p.StockQuantity, p.LowStockThreshold,
                p.StockQuantity <= p.LowStockThreshold, p.IsAvailable))
            .ToListAsync(ct);
    }

    public async Task<InventoryItemDto> AdjustAsync(int productId, StockAdjustmentRequest r, CancellationToken ct)
    {
        if (!await db.Products.AnyAsync(p => p.Id == productId && !p.IsDeleted, ct)) throw new NotFoundException("Product", productId);

        await db.InTransactionAsync(async token =>
        {
            if (!await stockLedger.TryAdjustAsync(productId, r.QuantityChange, token))
                throw new ConflictException("inventory.negative_stock", "That adjustment would make stock negative.");
            await stockLedger.RecordAsync([(productId, r.QuantityChange)], r.Reason, null, currentUser.UserId, r.Note, token);
            await db.SaveChangesAsync(token);
            return true;
        }, ct);

        return (await ListAsync(false, ct)).First(i => i.ProductId == productId);
    }

    public async Task<PagedResult<InventoryTransactionDto>> HistoryAsync(int? productId, int page, int pageSize, CancellationToken ct)
    {
        var (p, size) = Paging.Normalize(page, pageSize, 25);
        var query = db.InventoryTransactions.AsNoTracking().AsQueryable();
        if (productId is not null) query = query.Where(t => t.ProductId == productId);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).Skip((p - 1) * size).Take(size)
            .Select(t => new
            {
                t.Id, t.ProductId, ProductName = t.Product!.Name, t.QuantityChange, t.StockAfter, t.Reason, t.Note, t.OrderId, t.CreatedAt
            }).ToListAsync(ct);

        var orderIds = rows.Where(r => r.OrderId != null).Select(r => r.OrderId!.Value).Distinct().ToList();
        var numbers = await db.Orders.Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.OrderNumber, ct);

        return new PagedResult<InventoryTransactionDto>(rows.Select(r => new InventoryTransactionDto(r.Id, r.ProductId, r.ProductName,
            r.QuantityChange, r.StockAfter, r.Reason, r.Note, r.OrderId is { } oid ? numbers.GetValueOrDefault(oid) : null, r.CreatedAt)).ToList(),
            p, size, total);
    }
}

public sealed class CouponService(IApplicationDbContext db, TimeProvider clock)
{
    public async Task<IReadOnlyList<CouponDto>> ListAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var rows = await db.Coupons.AsNoTracking().OrderByDescending(c => c.IsActive).ThenByDescending(c => c.EndsAt).ToListAsync(ct);
        return rows.Select(c => ToDto(c, now)).ToList();
    }

    /// <summary>Active, currently valid promotions for the storefront banner.</summary>
    public async Task<IReadOnlyList<PromoDto>> ListPublicAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return await db.Coupons.AsNoTracking()
            .Where(c => c.IsActive && c.StartsAt <= now && c.EndsAt >= now && (c.UsageLimit == null || c.UsedCount < c.UsageLimit))
            .OrderByDescending(c => c.Value)
            .Select(c => new PromoDto(c.Code, c.Description, c.DiscountType, c.Value, c.MaxDiscountAmount, c.MinOrderAmount, c.EndsAt))
            .ToListAsync(ct);
    }

    public async Task<CouponDto> CreateAsync(CouponUpsertRequest r, CancellationToken ct)
    {
        var code = r.Code.Trim().ToUpperInvariant();
        if (await db.Coupons.AnyAsync(c => c.Code == code, ct)) throw new ConflictException("coupon.duplicate", "A coupon with this code already exists.");
        var coupon = new Coupon { Code = code };
        Apply(coupon, r);
        db.Coupons.Add(coupon);
        await db.SaveChangesAsync(ct);
        return ToDto(coupon, clock.GetUtcNow().UtcDateTime);
    }

    public async Task<CouponDto> UpdateAsync(int id, CouponUpsertRequest r, CancellationToken ct)
    {
        var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Coupon", id);
        var code = r.Code.Trim().ToUpperInvariant();
        if (await db.Coupons.AnyAsync(c => c.Code == code && c.Id != id, ct)) throw new ConflictException("coupon.duplicate", "A coupon with this code already exists.");
        coupon.Code = code;
        Apply(coupon, r);
        await db.SaveChangesAsync(ct);
        return ToDto(coupon, clock.GetUtcNow().UtcDateTime);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Coupon", id);
        if (await db.Orders.AnyAsync(o => o.CouponId == id, ct))
        {
            coupon.IsActive = false; // used coupons are deactivated, not deleted, to keep order history meaningful
        }
        else
        {
            db.Coupons.Remove(coupon);
        }
        await db.SaveChangesAsync(ct);
    }

    private static void Apply(Coupon c, CouponUpsertRequest r)
    {
        c.Description = r.Description.Trim();
        c.DiscountType = r.DiscountType;
        c.Value = r.Value;
        c.MaxDiscountAmount = r.DiscountType == DiscountType.Percentage ? r.MaxDiscountAmount : null;
        c.MinOrderAmount = r.MinOrderAmount;
        c.StartsAt = DateTime.SpecifyKind(r.StartsAt, DateTimeKind.Utc);
        c.EndsAt = DateTime.SpecifyKind(r.EndsAt, DateTimeKind.Utc);
        c.UsageLimit = r.UsageLimit;
        c.PerUserLimit = r.PerUserLimit;
        c.IsActive = r.IsActive;
    }

    private static CouponDto ToDto(Coupon c, DateTime now) => new(
        c.Id, c.Code, c.Description, c.DiscountType, c.Value, c.MaxDiscountAmount, c.MinOrderAmount, c.StartsAt, c.EndsAt,
        c.UsageLimit, c.PerUserLimit, c.UsedCount, c.IsActive,
        c.IsActive && c.StartsAt <= now && c.EndsAt >= now && (c.UsageLimit is null || c.UsedCount < c.UsageLimit));
}
