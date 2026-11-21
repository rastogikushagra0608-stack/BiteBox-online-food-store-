using BiteBox.Domain.Entities;
using BiteBox.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiteBox.Infrastructure.Persistence.Configurations;

// Fluent mappings keep persistence concerns (column sizes, indexes, FK rules) out of the Domain layer.
// Comparable to JPA annotations, but declared outside the entity classes.

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.ToTable("users");
        b.Property(u => u.FullName).HasMaxLength(80).IsRequired();
        b.Property(u => u.PhoneNumber).HasMaxLength(15);
        b.HasIndex(u => u.CreatedAt);
    }
}

internal sealed class IdentityTableNames :
    IEntityTypeConfiguration<AppRole>, IEntityTypeConfiguration<IdentityUserRole<int>>, IEntityTypeConfiguration<IdentityUserClaim<int>>,
    IEntityTypeConfiguration<IdentityUserLogin<int>>, IEntityTypeConfiguration<IdentityUserToken<int>>, IEntityTypeConfiguration<IdentityRoleClaim<int>>
{
    public void Configure(EntityTypeBuilder<AppRole> b) => b.ToTable("roles");
    public void Configure(EntityTypeBuilder<IdentityUserRole<int>> b) => b.ToTable("user_roles");
    public void Configure(EntityTypeBuilder<IdentityUserClaim<int>> b) => b.ToTable("user_claims");
    public void Configure(EntityTypeBuilder<IdentityUserLogin<int>> b) => b.ToTable("user_logins");
    public void Configure(EntityTypeBuilder<IdentityUserToken<int>> b) => b.ToTable("user_tokens");
    public void Configure(EntityTypeBuilder<IdentityRoleClaim<int>> b) => b.ToTable("role_claims");
}

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.ToTable("categories");
        b.Property(c => c.Name).HasMaxLength(60).IsRequired();
        b.Property(c => c.Slug).HasMaxLength(80).IsRequired();
        b.Property(c => c.Description).HasMaxLength(300);
        b.Property(c => c.ImageUrl).HasMaxLength(500);
        b.HasIndex(c => c.Slug).IsUnique();
        b.Ignore(c => c.DomainEvents);
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("products", t =>
        {
            // Last line of defence: the database itself refuses negative stock or prices.
            t.HasCheckConstraint("CK_products_stock_non_negative", "`StockQuantity` >= 0");
            t.HasCheckConstraint("CK_products_price_positive", "`Price` > 0");
        });
        b.Property(p => p.Name).HasMaxLength(100).IsRequired();
        b.Property(p => p.Slug).HasMaxLength(120).IsRequired();
        b.Property(p => p.Description).HasMaxLength(600).IsRequired();
        b.Property(p => p.ImageUrl).HasMaxLength(500);
        b.Property(p => p.Tags).HasMaxLength(200).IsRequired();
        b.Property(p => p.AverageRating).HasPrecision(2, 1);

        b.HasIndex(p => p.Slug).IsUnique();
        b.HasIndex(p => new { p.CategoryId, p.IsDeleted, p.IsAvailable }); // menu filtering
        b.HasIndex(p => p.Price);
        b.HasIndex(p => p.IsFeatured);

        b.HasOne(p => p.Category).WithMany(c => c.Products).HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.Ignore(p => p.IsLowStock);
        b.Ignore(p => p.CanBeOrdered);
        b.Ignore(p => p.DomainEvents);
    }
}

internal sealed class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> b)
    {
        b.ToTable("addresses");
        b.Property(a => a.Label).HasMaxLength(30).IsRequired();
        b.Property(a => a.RecipientName).HasMaxLength(80).IsRequired();
        b.Property(a => a.Phone).HasMaxLength(15).IsRequired();
        b.Property(a => a.Line1).HasMaxLength(150).IsRequired();
        b.Property(a => a.Line2).HasMaxLength(150);
        b.Property(a => a.City).HasMaxLength(60).IsRequired();
        b.Property(a => a.Pincode).HasMaxLength(6).IsRequired();
        b.Property(a => a.Landmark).HasMaxLength(100);
        b.HasIndex(a => a.UserId);
        b.HasOne<AppUser>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(a => a.DomainEvents);
    }
}

internal sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> b)
    {
        b.ToTable("cart_items", t => t.HasCheckConstraint("CK_cart_items_quantity", "`Quantity` BETWEEN 1 AND 20"));
        b.HasIndex(c => new { c.UserId, c.ProductId }).IsUnique();
        b.HasOne(c => c.Product).WithMany().HasForeignKey(c => c.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(c => c.DomainEvents);
    }
}

internal sealed class FavoriteConfiguration : IEntityTypeConfiguration<Favorite>
{
    public void Configure(EntityTypeBuilder<Favorite> b)
    {
        b.ToTable("favorites");
        b.HasKey(f => new { f.UserId, f.ProductId }); // composite key = natural uniqueness
        b.HasOne(f => f.Product).WithMany().HasForeignKey(f => f.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<AppUser>().WithMany().HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> b)
    {
        b.ToTable("reviews", t => t.HasCheckConstraint("CK_reviews_rating", "`Rating` BETWEEN 1 AND 5"));
        b.Property(r => r.AuthorName).HasMaxLength(80).IsRequired();
        b.Property(r => r.Comment).HasMaxLength(500);
        b.HasIndex(r => new { r.ProductId, r.UserId }).IsUnique(); // one review per dish per customer
        b.HasOne(r => r.Product).WithMany().HasForeignKey(r => r.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<AppUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(r => r.DomainEvents);
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notifications");
        b.Property(n => n.Title).HasMaxLength(100).IsRequired();
        b.Property(n => n.Message).HasMaxLength(400).IsRequired();
        b.Property(n => n.Link).HasMaxLength(200);
        b.HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAt });
        b.HasOne<AppUser>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(n => n.DomainEvents);
    }
}

internal sealed class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> b)
    {
        b.ToTable("inventory_transactions");
        b.Property(t => t.Note).HasMaxLength(200);
        b.HasIndex(t => new { t.ProductId, t.CreatedAt });
        b.HasIndex(t => t.OrderId);
        b.HasOne(t => t.Product).WithMany().HasForeignKey(t => t.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.Ignore(t => t.DomainEvents);
    }
}

internal sealed class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> b)
    {
        b.ToTable("coupons", t => t.HasCheckConstraint("CK_coupons_used", "`UsedCount` >= 0"));
        b.Property(c => c.Code).HasMaxLength(30).IsRequired();
        b.Property(c => c.Description).HasMaxLength(150).IsRequired();
        b.HasIndex(c => c.Code).IsUnique();
        b.Ignore(c => c.DomainEvents);
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("orders");
        b.Property(o => o.OrderNumber).HasMaxLength(20).IsRequired();
        b.Property(o => o.CustomerName).HasMaxLength(80).IsRequired();
        b.Property(o => o.CustomerPhone).HasMaxLength(15).IsRequired();
        b.Property(o => o.CouponCode).HasMaxLength(30);
        b.Property(o => o.Notes).HasMaxLength(300);
        b.Property(o => o.CancellationReason).HasMaxLength(200);
        b.Property(o => o.IdempotencyKey).HasMaxLength(64).IsRequired();

        b.HasIndex(o => o.OrderNumber).IsUnique();
        b.HasIndex(o => new { o.CustomerId, o.IdempotencyKey }).IsUnique(); // duplicate-submission guard
        b.HasIndex(o => new { o.CustomerId, o.PlacedAt });
        b.HasIndex(o => new { o.Status, o.PlacedAt }); // kitchen queue & dashboard
        b.HasIndex(o => o.PlacedAt);

        b.HasOne<AppUser>().WithMany().HasForeignKey(o => o.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Coupon>().WithMany().HasForeignKey(o => o.CouponId).OnDelete(DeleteBehavior.SetNull);

        // Value object stored in the orders table itself (columns DeliveryAddress_Line1, ...).
        b.OwnsOne(o => o.DeliveryAddress, a =>
        {
            a.Property(x => x.RecipientName).HasMaxLength(80);
            a.Property(x => x.Phone).HasMaxLength(15);
            a.Property(x => x.Line1).HasMaxLength(150);
            a.Property(x => x.Line2).HasMaxLength(150);
            a.Property(x => x.City).HasMaxLength(60);
            a.Property(x => x.Pincode).HasMaxLength(6);
            a.Property(x => x.Landmark).HasMaxLength(100);
        });

        b.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(o => o.StatusHistory).WithOne().HasForeignKey(h => h.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(o => o.Payment).WithOne().HasForeignKey<Payment>(p => p.OrderId).OnDelete(DeleteBehavior.Cascade);

        b.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(o => o.StatusHistory).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(o => o.IsActive);
        b.Ignore(o => o.DomainEvents);
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.ToTable("order_items");
        b.Property(i => i.ProductName).HasMaxLength(100).IsRequired();
        b.HasIndex(i => i.ProductId);
        // Restrict: a product that appears in any order can only be soft-deleted.
        b.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.Ignore(i => i.DomainEvents);
    }
}

internal sealed class OrderStatusChangeConfiguration : IEntityTypeConfiguration<OrderStatusChange>
{
    public void Configure(EntityTypeBuilder<OrderStatusChange> b)
    {
        b.ToTable("order_status_history");
        b.Property(h => h.Note).HasMaxLength(200);
        b.HasIndex(h => new { h.OrderId, h.ChangedAt });
        b.Ignore(h => h.DomainEvents);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments");
        b.Property(p => p.ProviderReference).HasMaxLength(100);
        b.Property(p => p.FailureReason).HasMaxLength(200);
        b.HasIndex(p => p.OrderId).IsUnique();
        b.HasIndex(p => p.Status);
        b.Ignore(p => p.DomainEvents);
    }
}
