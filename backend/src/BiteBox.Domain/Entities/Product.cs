using BiteBox.Domain.Common;
using BiteBox.Domain.Enums;
using BiteBox.Domain.Exceptions;

namespace BiteBox.Domain.Entities;

/// <summary>
/// A dish on the BiteBox menu. Stock is tracked as "portions available" which the kitchen
/// restocks; order placement decrements it atomically in the database to prevent overselling.
/// </summary>
public class Product : AuditableEntity
{
    private Product() { } // EF Core

    public Product(int categoryId, string name, string slug, string description, decimal price, int stockQuantity)
    {
        CategoryId = categoryId;
        Name = name;
        Slug = slug;
        Description = description;
        SetPrice(price);
        if (stockQuantity < 0) throw new DomainException("product.invalid_stock", "Stock cannot be negative.");
        StockQuantity = stockQuantity;
    }

    public int CategoryId { get; set; }
    public Category? Category { get; private set; }

    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }

    public decimal Price { get; private set; }
    public bool IsVegetarian { get; set; }
    public SpiceLevel SpiceLevel { get; set; }
    public int? Calories { get; set; }
    public int PrepTimeMinutes { get; set; } = 15;
    public int ServesCount { get; set; } = 1;

    /// <summary>Comma-separated, lowercase tags used for search and BiteBot (e.g. "healthy,protein,grilled").</summary>
    public string Tags { get; set; } = string.Empty;

    public bool IsAvailable { get; set; } = true;
    public bool IsFeatured { get; set; }

    /// <summary>Soft delete: products referenced by historical orders are never physically removed.</summary>
    public bool IsDeleted { get; private set; }

    public int StockQuantity { get; private set; }
    public int LowStockThreshold { get; set; } = 10;

    public decimal AverageRating { get; private set; }
    public int ReviewCount { get; private set; }

    public bool IsLowStock => StockQuantity <= LowStockThreshold;
    public bool CanBeOrdered => IsAvailable && !IsDeleted && StockQuantity > 0;

    public void SetPrice(decimal price)
    {
        if (price <= 0 || price > 100_000)
            throw new DomainException("product.invalid_price", "Price must be greater than zero and at most ₹1,00,000.");
        Price = Money.Round(price);
    }

    // Note: there is intentionally no "StockQuantity = x" setter. Every stock change is an atomic
    // relative UPDATE issued by StockLedger, so admin edits can never overwrite a concurrent order's decrement.

    public void SetRatingSummary(decimal average, int count)
    {
        AverageRating = Math.Round(average, 1);
        ReviewCount = count;
    }

    public void SoftDelete()
    {
        IsDeleted = true;
        IsAvailable = false;
        IsFeatured = false;
    }
}
