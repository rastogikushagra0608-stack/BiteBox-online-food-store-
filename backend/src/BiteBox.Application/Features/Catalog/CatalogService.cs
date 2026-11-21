using BiteBox.Application.Abstractions;
using BiteBox.Application.Common;
using BiteBox.Domain.Entities;
using BiteBox.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BiteBox.Application.Features.Catalog;

public sealed record CategoryDto(int Id, string Name, string Slug, string? Description, string? ImageUrl, int ProductCount);

public sealed record ProductCardDto(
    int Id, string Name, string Slug, string Description, string? ImageUrl, decimal Price,
    bool IsVegetarian, SpiceLevel SpiceLevel, string CategoryName, string CategorySlug,
    decimal AverageRating, int ReviewCount, bool InStock, int? OnlyLeft, int PrepTimeMinutes,
    bool IsFeatured, int ServesCount, string[] Tags);

public sealed record ProductDetailDto(
    int Id, string Name, string Slug, string Description, string? ImageUrl, decimal Price,
    bool IsVegetarian, SpiceLevel SpiceLevel, int CategoryId, string CategoryName, string CategorySlug,
    decimal AverageRating, int ReviewCount, bool InStock, int? OnlyLeft, int PrepTimeMinutes,
    int? Calories, int ServesCount, string[] Tags, bool IsFavorite);

public enum ProductSort { Popular, PriceLowToHigh, PriceHighToLow, Rating, Newest }

public sealed class ProductQuery
{
    public string? Search { get; set; }
    public string? Category { get; set; }
    public bool? Vegetarian { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public SpiceLevel? MaxSpice { get; set; }
    public bool? FeaturedOnly { get; set; }
    public bool? InStockOnly { get; set; }
    public ProductSort Sort { get; set; } = ProductSort.Popular;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 12;
}

/// <summary>
/// Read-side use cases for the public menu. Queries project straight into DTOs (no tracking,
/// no entity leakage), which is the "query" half of a lightweight CQRS split.
/// </summary>
public sealed class CatalogService(IApplicationDbContext db, ICurrentUser currentUser)
{
    /// <summary>Show "only N left" when stock is at or below this.</summary>
    public const int ScarcityThreshold = 5;

    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken ct) =>
        await db.Categories.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Slug, c.Description, c.ImageUrl,
                c.Products.Count(p => !p.IsDeleted)))
            .ToListAsync(ct);

    public async Task<PagedResult<ProductCardDto>> SearchProductsAsync(ProductQuery query, CancellationToken ct)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var products = VisibleProducts();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Each word must match name, description, tags or category (AND semantics feel natural for search).
            foreach (var term in query.Search.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5))
            {
                var pattern = $"%{EscapeLike(term)}%";
                products = products.Where(p =>
                    EF.Functions.Like(p.Name, pattern) ||
                    EF.Functions.Like(p.Description, pattern) ||
                    EF.Functions.Like(p.Tags, pattern) ||
                    EF.Functions.Like(p.Category!.Name, pattern));
            }
        }

        if (!string.IsNullOrWhiteSpace(query.Category)) products = products.Where(p => p.Category!.Slug == query.Category);
        if (query.Vegetarian is not null) products = products.Where(p => p.IsVegetarian == query.Vegetarian);
        if (query.MinPrice is not null) products = products.Where(p => p.Price >= query.MinPrice);
        if (query.MaxPrice is not null) products = products.Where(p => p.Price <= query.MaxPrice);
        if (query.MaxSpice is not null) products = products.Where(p => p.SpiceLevel <= query.MaxSpice);
        if (query.FeaturedOnly == true) products = products.Where(p => p.IsFeatured);
        if (query.InStockOnly == true) products = products.Where(p => p.IsAvailable && p.StockQuantity > 0);

        var total = await products.CountAsync(ct);

        products = query.Sort switch
        {
            ProductSort.PriceLowToHigh => products.OrderBy(p => p.Price).ThenBy(p => p.Id),
            ProductSort.PriceHighToLow => products.OrderByDescending(p => p.Price).ThenBy(p => p.Id),
            ProductSort.Rating => products.OrderByDescending(p => p.AverageRating).ThenByDescending(p => p.ReviewCount),
            ProductSort.Newest => products.OrderByDescending(p => p.CreatedAt),
            // "Popular" = most portions sold, featured items first on ties.
            _ => products
                .OrderByDescending(p => db.OrderItems.Where(i => i.ProductId == p.Id).Sum(i => (int?)i.Quantity) ?? 0)
                .ThenByDescending(p => p.IsFeatured).ThenBy(p => p.Id)
        };

        var rows = await products.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<ProductCardDto>(rows.Select(ToCard).ToList(), page, pageSize, total);
    }

    public async Task<ProductDetailDto> GetProductAsync(string slug, CancellationToken ct)
    {
        var p = await VisibleProducts().FirstOrDefaultAsync(x => x.Slug == slug, ct)
                ?? throw new NotFoundException("Product", slug);

        var isFavorite = currentUser.UserId is { } uid &&
                         await db.Favorites.AnyAsync(f => f.UserId == uid && f.ProductId == p.Id, ct);

        return new ProductDetailDto(p.Id, p.Name, p.Slug, p.Description, p.ImageUrl, p.Price, p.IsVegetarian,
            p.SpiceLevel, p.CategoryId, p.Category!.Name, p.Category.Slug, p.AverageRating, p.ReviewCount,
            p.CanBeOrdered, OnlyLeft(p), p.PrepTimeMinutes, p.Calories, p.ServesCount, SplitTags(p.Tags), isFavorite);
    }

    public async Task<IReadOnlyList<ProductCardDto>> GetRelatedAsync(string slug, CancellationToken ct)
    {
        var source = await db.Products.AsNoTracking().Where(p => p.Slug == slug)
            .Select(p => new { p.Id, p.CategoryId, p.IsVegetarian }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Product", slug);

        var rows = await VisibleProducts()
            .Where(p => p.Id != source.Id && p.CategoryId == source.CategoryId && p.IsAvailable)
            .OrderByDescending(p => p.IsVegetarian == source.IsVegetarian).ThenByDescending(p => p.AverageRating)
            .Take(4).ToListAsync(ct);
        return rows.Select(ToCard).ToList();
    }

    private IQueryable<Product> VisibleProducts() =>
        db.Products.AsNoTracking().Include(p => p.Category).Where(p => !p.IsDeleted && p.Category!.IsActive);

    internal static ProductCardDto ToCard(Product p) => new(
        p.Id, p.Name, p.Slug, p.Description, p.ImageUrl, p.Price, p.IsVegetarian, p.SpiceLevel,
        p.Category?.Name ?? string.Empty, p.Category?.Slug ?? string.Empty, p.AverageRating, p.ReviewCount,
        p.CanBeOrdered, OnlyLeft(p), p.PrepTimeMinutes, p.IsFeatured, p.ServesCount, SplitTags(p.Tags));

    private static int? OnlyLeft(Product p) => p.CanBeOrdered && p.StockQuantity <= ScarcityThreshold ? p.StockQuantity : null;

    internal static string[] SplitTags(string tags) =>
        tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string EscapeLike(string term) => term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
