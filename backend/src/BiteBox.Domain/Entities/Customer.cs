using BiteBox.Domain.Common;

namespace BiteBox.Domain.Entities;

/// <summary>A saved delivery address belonging to a user.</summary>
public class Address : AuditableEntity
{
    public int UserId { get; set; }
    public string Label { get; set; } = "Home";
    public string RecipientName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string Pincode { get; set; } = string.Empty;
    public string? Landmark { get; set; }
    public bool IsDefault { get; set; }
}

/// <summary>Server-side cart line. One row per (user, product).</summary>
public class CartItem : Entity
{
    public int UserId { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int Quantity { get; set; }
    public DateTime AddedAt { get; set; }

    public const int MaxQuantityPerItem = 20;
}

public class Favorite
{
    public int UserId { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Review : AuditableEntity
{
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int UserId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public int Rating { get; private set; }
    public string? Comment { get; set; }

    public void SetRating(int rating)
    {
        if (rating is < 1 or > 5) throw new Exceptions.DomainException("review.invalid_rating", "Rating must be between 1 and 5.");
        Rating = rating;
    }
}

public class Notification : Entity
{
    public int UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Link { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class InventoryTransaction : Entity
{
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int QuantityChange { get; set; }
    public int StockAfter { get; set; }
    public Enums.InventoryReason Reason { get; set; }
    public string? Note { get; set; }
    public int? OrderId { get; set; }
    public int? PerformedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
