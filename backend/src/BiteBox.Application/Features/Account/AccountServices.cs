using BiteBox.Application.Abstractions;
using BiteBox.Application.Common;
using BiteBox.Application.Features.Catalog;
using BiteBox.Domain.Entities;
using BiteBox.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BiteBox.Application.Features.Account;

public sealed record AddressDto(int Id, string Label, string RecipientName, string Phone, string Line1, string? Line2,
    string City, string Pincode, string? Landmark, bool IsDefault);

public sealed record AddressRequest(string Label, string RecipientName, string Phone, string Line1, string? Line2,
    string City, string Pincode, string? Landmark, bool IsDefault);

public sealed record NotificationDto(int Id, string Title, string Message, string? Link, bool IsRead, DateTime CreatedAt);

public sealed record NotificationFeedDto(IReadOnlyList<NotificationDto> Items, int UnreadCount);

public sealed class AddressService(IApplicationDbContext db, ICurrentUser currentUser)
{
    public const int MaxAddresses = 10;

    public async Task<IReadOnlyList<AddressDto>> ListAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        return await db.Addresses.AsNoTracking().Where(a => a.UserId == userId)
            .OrderByDescending(a => a.IsDefault).ThenBy(a => a.Id)
            .Select(a => ToDto(a)).ToListAsync(ct);
    }

    public async Task<AddressDto> CreateAsync(AddressRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var count = await db.Addresses.CountAsync(a => a.UserId == userId, ct);
        if (count >= MaxAddresses) throw new BadRequestException("address.limit", $"You can save up to {MaxAddresses} addresses.");

        var address = new Address { UserId = userId };
        Apply(address, request);
        address.IsDefault = request.IsDefault || count == 0; // first address becomes default
        if (address.IsDefault) await ClearDefaultAsync(userId, ct);

        db.Addresses.Add(address);
        await db.SaveChangesAsync(ct);
        return ToDto(address);
    }

    public async Task<AddressDto> UpdateAsync(int id, AddressRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var address = await db.Addresses.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct)
                      ?? throw new NotFoundException("Address", id);
        if (request.IsDefault && !address.IsDefault) await ClearDefaultAsync(userId, ct);
        Apply(address, request);
        address.IsDefault = request.IsDefault || address.IsDefault;
        await db.SaveChangesAsync(ct);
        return ToDto(address);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var address = await db.Addresses.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct)
                      ?? throw new NotFoundException("Address", id);
        db.Addresses.Remove(address);
        await db.SaveChangesAsync(ct);

        if (address.IsDefault)
        {
            var next = await db.Addresses.Where(a => a.UserId == userId).OrderBy(a => a.Id).FirstOrDefaultAsync(ct);
            if (next is not null) { next.IsDefault = true; await db.SaveChangesAsync(ct); }
        }
    }

    private Task ClearDefaultAsync(int userId, CancellationToken ct) =>
        db.Addresses.Where(a => a.UserId == userId && a.IsDefault).ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDefault, false), ct);

    private static void Apply(Address a, AddressRequest r)
    {
        a.Label = r.Label.Trim();
        a.RecipientName = r.RecipientName.Trim();
        a.Phone = r.Phone.Trim();
        a.Line1 = r.Line1.Trim();
        a.Line2 = string.IsNullOrWhiteSpace(r.Line2) ? null : r.Line2.Trim();
        a.City = r.City.Trim();
        a.Pincode = r.Pincode.Trim();
        a.Landmark = string.IsNullOrWhiteSpace(r.Landmark) ? null : r.Landmark.Trim();
    }

    private static AddressDto ToDto(Address a) =>
        new(a.Id, a.Label, a.RecipientName, a.Phone, a.Line1, a.Line2, a.City, a.Pincode, a.Landmark, a.IsDefault);
}

public sealed class FavoriteService(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<IReadOnlyList<ProductCardDto>> ListAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var products = await db.Favorites.AsNoTracking().Where(f => f.UserId == userId && !f.Product!.IsDeleted)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => f.Product!).Include(p => p.Category).ToListAsync(ct);
        return products.Select(CatalogService.ToCard).ToList();
    }

    public async Task<IReadOnlyList<int>> ListIdsAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        return await db.Favorites.Where(f => f.UserId == userId).Select(f => f.ProductId).ToListAsync(ct);
    }

    public async Task AddAsync(int productId, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        if (!await db.Products.AnyAsync(p => p.Id == productId && !p.IsDeleted, ct)) throw new NotFoundException("Product", productId);
        if (await db.Favorites.AnyAsync(f => f.UserId == userId && f.ProductId == productId, ct)) return; // idempotent
        db.Favorites.Add(new Favorite { UserId = userId, ProductId = productId, CreatedAt = clock.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(int productId, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        await db.Favorites.Where(f => f.UserId == userId && f.ProductId == productId).ExecuteDeleteAsync(ct);
    }
}

public sealed class NotificationService(IApplicationDbContext db, ICurrentUser currentUser)
{
    public async Task<NotificationFeedDto> ListAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var items = await db.Notifications.AsNoTracking().Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Take(30)
            .Select(n => new NotificationDto(n.Id, n.Title, n.Message, n.Link, n.IsRead, n.CreatedAt)).ToListAsync(ct);
        var unread = await db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);
        return new NotificationFeedDto(items, unread);
    }

    public async Task MarkAllReadAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        await db.Notifications.Where(n => n.UserId == userId && !n.IsRead).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
    }
}

public sealed record ReviewDto(int Id, int ProductId, string AuthorName, int Rating, string? Comment, DateTime CreatedAt);
public sealed record CreateReviewRequest(int Rating, string? Comment);
public sealed record ReviewSummaryDto(IReadOnlyList<ReviewDto> Items, decimal AverageRating, int ReviewCount, bool CanReview, bool HasReviewed);

/// <summary>Only customers who have received a product may review it, once per product.</summary>
public sealed class ReviewService(IApplicationDbContext db, ICurrentUser currentUser, IIdentityService identity)
{
    public async Task<ReviewSummaryDto> ListAsync(int productId, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().Where(p => p.Id == productId && !p.IsDeleted)
            .Select(p => new { p.AverageRating, p.ReviewCount }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Product", productId);

        var items = await db.Reviews.AsNoTracking().Where(r => r.ProductId == productId)
            .OrderByDescending(r => r.CreatedAt).Take(50)
            .Select(r => new ReviewDto(r.Id, r.ProductId, r.AuthorName, r.Rating, r.Comment, r.CreatedAt)).ToListAsync(ct);

        var canReview = false;
        var hasReviewed = false;
        if (currentUser.UserId is { } uid)
        {
            hasReviewed = await db.Reviews.AnyAsync(r => r.ProductId == productId && r.UserId == uid, ct);
            canReview = !hasReviewed && await HasReceivedAsync(uid, productId, ct);
        }

        return new ReviewSummaryDto(items, product.AverageRating, product.ReviewCount, canReview, hasReviewed);
    }

    public async Task<IReadOnlyList<ReviewDto>> ListMineAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        return await db.Reviews.AsNoTracking().Where(r => r.UserId == userId).OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReviewDto(r.Id, r.ProductId, r.Product!.Name, r.Rating, r.Comment, r.CreatedAt)).ToListAsync(ct);
    }

    public async Task<ReviewDto> CreateAsync(int productId, CreateReviewRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId && !p.IsDeleted, ct)
                      ?? throw new NotFoundException("Product", productId);

        if (await db.Reviews.AnyAsync(r => r.ProductId == productId && r.UserId == userId, ct))
            throw new ConflictException("review.duplicate", "You have already reviewed this dish.");
        if (!await HasReceivedAsync(userId, productId, ct))
            throw new ForbiddenException("You can review a dish after your order containing it has been delivered.");

        var user = await identity.GetUserAsync(userId, ct) ?? throw new UnauthorizedException();
        var review = new Review
        {
            ProductId = productId,
            UserId = userId,
            AuthorName = ShortName(user.FullName),
            Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim()
        };
        review.SetRating(request.Rating);
        db.Reviews.Add(review);
        await db.SaveChangesAsync(ct);

        // Denormalised rating summary keeps menu listing queries cheap.
        var stats = await db.Reviews.Where(r => r.ProductId == productId)
            .GroupBy(r => r.ProductId)
            .Select(g => new { Avg = g.Average(r => (decimal)r.Rating), Count = g.Count() }).FirstAsync(ct);
        product.SetRatingSummary(stats.Avg, stats.Count);
        await db.SaveChangesAsync(ct);

        return new ReviewDto(review.Id, productId, review.AuthorName, review.Rating, review.Comment, review.CreatedAt);
    }

    private Task<bool> HasReceivedAsync(int userId, int productId, CancellationToken ct) =>
        db.Orders.AnyAsync(o => o.CustomerId == userId
                                && (o.Status == OrderStatus.Delivered || o.Status == OrderStatus.PickedUp)
                                && o.Items.Any(i => i.ProductId == productId), ct);

    /// <summary>"Priya Sharma" → "Priya S." (privacy for public reviews).</summary>
    private static string ShortName(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "BiteBox customer",
            1 => parts[0],
            _ => $"{parts[0]} {parts[^1][0]}."
        };
    }
}
