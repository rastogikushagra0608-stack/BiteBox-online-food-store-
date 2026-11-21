namespace BiteBox.Application.Common;

/// <summary>Application-level failures. The API's exception middleware maps each to an HTTP status.</summary>
public abstract class AppException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>404</summary>
public sealed class NotFoundException(string resource, object key)
    : AppException("not_found", $"{resource} '{key}' was not found.");

/// <summary>409</summary>
public sealed class ConflictException(string code, string message) : AppException(code, message);

/// <summary>403 — authenticated but not allowed.</summary>
public sealed class ForbiddenException(string message = "You do not have permission to perform this action.")
    : AppException("forbidden", message);

/// <summary>401</summary>
public sealed class UnauthorizedException(string message = "Please sign in to continue.")
    : AppException("unauthorized", message);

/// <summary>400 — a single field-level problem detected inside a use case.</summary>
public sealed class BadRequestException(string code, string message) : AppException(code, message);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public static class Paging
{
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Normalize(int page, int pageSize, int defaultSize = 12) =>
        (Math.Max(1, page), pageSize <= 0 ? defaultSize : Math.Min(pageSize, MaxPageSize));
}

public static class Slug
{
    public static string From(string text)
    {
        var chars = text.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }
}

/// <summary>Business configuration bound from the "BiteBox:Pricing" section of appsettings.</summary>
public sealed class PricingOptions
{
    public const string Section = "BiteBox:Pricing";

    /// <summary>GST rate applied to (subtotal - discount). 0.05 = 5%.</summary>
    public decimal TaxRate { get; set; } = 0.05m;
    public decimal DeliveryFee { get; set; } = 40m;
    public decimal FreeDeliveryThreshold { get; set; } = 499m;
    public decimal MinimumOrderAmount { get; set; } = 99m;
}
