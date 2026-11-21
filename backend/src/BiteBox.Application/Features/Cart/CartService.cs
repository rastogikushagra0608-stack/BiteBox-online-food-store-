using BiteBox.Application.Abstractions;
using BiteBox.Application.Common;
using BiteBox.Domain.Common;
using BiteBox.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BiteBox.Application.Features.Cart;

public sealed record CartItemDto(
    int ProductId, string Name, string Slug, string? ImageUrl, decimal UnitPrice, int Quantity,
    decimal LineTotal, bool IsVegetarian, bool InStock, int AvailableQuantity);

public sealed record CartDto(IReadOnlyList<CartItemDto> Items, int ItemCount, decimal Subtotal, bool HasUnavailableItems);

public sealed record CartLineRequest(int ProductId, int Quantity);

/// <summary>
/// Server-side cart for signed-in customers. Prices shown here are always the *current* menu price;
/// the client never supplies prices. Guests keep a local cart that is merged on sign-in.
/// </summary>
public sealed class CartService(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<CartDto> GetAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var items = await db.CartItems.AsNoTracking()
            .Where(c => c.UserId == userId)
            .Include(c => c.Product)
            .OrderBy(c => c.AddedAt)
            .ToListAsync(ct);

        var dtos = items.Select(c =>
        {
            var p = c.Product!;
            var inStock = p.CanBeOrdered && p.StockQuantity >= c.Quantity;
            return new CartItemDto(p.Id, p.Name, p.Slug, p.ImageUrl, p.Price, c.Quantity,
                Money.Round(p.Price * c.Quantity), p.IsVegetarian, inStock, p.CanBeOrdered ? p.StockQuantity : 0);
        }).ToList();

        return new CartDto(dtos, dtos.Sum(d => d.Quantity), Money.Round(dtos.Sum(d => d.LineTotal)), dtos.Any(d => !d.InStock));
    }

    public async Task<CartDto> AddAsync(CartLineRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var product = await GetOrderableProductAsync(request.ProductId, ct);

        var existing = await db.CartItems.FirstOrDefaultAsync(c => c.UserId == userId && c.ProductId == product.Id, ct);
        var newQuantity = (existing?.Quantity ?? 0) + request.Quantity;
        EnsureQuantityAllowed(product, newQuantity);

        if (existing is null)
            db.CartItems.Add(new CartItem { UserId = userId, ProductId = product.Id, Quantity = newQuantity, AddedAt = clock.GetUtcNow().UtcDateTime });
        else
            existing.Quantity = newQuantity;

        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    public async Task<CartDto> SetQuantityAsync(int productId, int quantity, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var item = await db.CartItems.Include(c => c.Product).FirstOrDefaultAsync(c => c.UserId == userId && c.ProductId == productId, ct)
                   ?? throw new NotFoundException("Cart item", productId);

        if (quantity <= 0)
        {
            db.CartItems.Remove(item);
        }
        else
        {
            EnsureQuantityAllowed(item.Product!, quantity);
            item.Quantity = quantity;
        }

        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    public async Task<CartDto> RemoveAsync(int productId, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        await db.CartItems.Where(c => c.UserId == userId && c.ProductId == productId).ExecuteDeleteAsync(ct);
        return await GetAsync(ct);
    }

    public async Task ClearAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        await db.CartItems.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Merges a guest cart into the user's cart after sign-in. Unorderable products are skipped and
    /// quantities are capped silently, because the guest could not have known about stock changes.
    /// </summary>
    public async Task<CartDto> MergeAsync(IReadOnlyList<CartLineRequest> lines, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var ids = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var existing = await db.CartItems.Where(c => c.UserId == userId).ToDictionaryAsync(c => c.ProductId, ct);
        var now = clock.GetUtcNow().UtcDateTime;

        foreach (var line in lines.GroupBy(l => l.ProductId).Select(g => new CartLineRequest(g.Key, g.Sum(x => x.Quantity))))
        {
            if (!products.TryGetValue(line.ProductId, out var product) || !product.CanBeOrdered || line.Quantity <= 0) continue;

            var cap = Math.Min(CartItem.MaxQuantityPerItem, product.StockQuantity);
            if (existing.TryGetValue(product.Id, out var item))
                item.Quantity = Math.Min(cap, Math.Max(item.Quantity, line.Quantity));
            else
                db.CartItems.Add(new CartItem { UserId = userId, ProductId = product.Id, Quantity = Math.Min(cap, line.Quantity), AddedAt = now });
        }

        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    private async Task<Product> GetOrderableProductAsync(int productId, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId && !p.IsDeleted, ct)
                      ?? throw new NotFoundException("Product", productId);
        if (!product.CanBeOrdered)
            throw new ConflictException("product.unavailable", $"{product.Name} is currently unavailable.");
        return product;
    }

    private static void EnsureQuantityAllowed(Product product, int quantity)
    {
        if (quantity > CartItem.MaxQuantityPerItem)
            throw new BadRequestException("cart.max_quantity", $"You can order at most {CartItem.MaxQuantityPerItem} of an item.");
        if (quantity > product.StockQuantity)
            throw new ConflictException("cart.insufficient_stock",
                product.StockQuantity == 0 ? $"{product.Name} is sold out." : $"Only {product.StockQuantity} portions of {product.Name} are left.");
    }
}
