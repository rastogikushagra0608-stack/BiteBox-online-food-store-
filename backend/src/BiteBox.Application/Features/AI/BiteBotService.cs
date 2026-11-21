using System.Text;
using BiteBox.Application.Abstractions;
using BiteBox.Application.Features.Catalog;
using BiteBox.Domain.Entities;
using BiteBox.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BiteBox.Application.Features.AI;

public sealed record BiteBotRequest(string Message);

public sealed record BiteBotSuggestion(ProductCardDto Product, string Reason);

public sealed record BiteBotComboItem(ProductCardDto Product, int Quantity);

public sealed record BiteBotCombo(IReadOnlyList<BiteBotComboItem> Items, decimal FoodTotal, string Summary);

public sealed record BiteBotResponse(
    string Reply,
    IReadOnlyList<BiteBotSuggestion> Suggestions,
    BiteBotCombo? Combo,
    IReadOnlyList<string> UnderstoodAs,
    IReadOnlyList<string> FollowUps,
    string Engine);

public interface IBiteBotService
{
    Task<BiteBotResponse> AskAsync(BiteBotRequest request, CancellationToken ct);
}

/// <summary>
/// BiteBot: product-grounded food recommendations.
///
/// Pipeline: text → <see cref="FoodIntent"/> (LLM adapter if configured, else rules) → hard filters on the
/// real catalogue → scoring (relevance + rating + popularity + personal history) → optional combo that
/// fits the budget → reply composed from templates using only database facts.
/// Because the reply and every product, price and stock flag come from our own data, BiteBot cannot
/// "hallucinate" a dish or a price.
/// </summary>
public sealed class BiteBotService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IEnumerable<IFoodIntentExtractor> extractors,
    ILogger<BiteBotService> logger) : IBiteBotService
{
    private const int MaxSuggestions = 4;
    private static readonly string[] SideCategories = ["starters", "desserts", "beverages"];
    private static readonly string[] HealthyTags = ["healthy", "protein", "salad", "grilled", "low-cal", "high-fibre", "fresh"];
    private static readonly string[] IndulgentTags = ["fried", "cheesy", "indulgent", "creamy"];

    public async Task<BiteBotResponse> AskAsync(BiteBotRequest request, CancellationToken ct)
    {
        var categories = await db.Categories.AsNoTracking().Where(c => c.IsActive).Select(c => c.Slug).ToListAsync(ct);

        var (intent, engine) = await InterpretAsync(request.Message, categories, ct);

        var products = await db.Products.AsNoTracking().Include(p => p.Category)
            .Where(p => !p.IsDeleted && p.IsAvailable && p.StockQuantity > 0 && p.Category!.IsActive)
            .ToListAsync(ct);

        var history = await LoadHistoryAsync(ct);
        var popularity = await db.OrderItems.AsNoTracking()
            .GroupBy(i => i.ProductId).Select(g => new { g.Key, Qty = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

        var (candidates, relaxed) = Filter(products, intent);
        var scored = candidates
            .Select(p => (Product: p, Score: Score(p, intent, history, popularity)))
            .OrderByDescending(x => x.Score).ThenBy(x => x.Product.Price)
            .ToList();

        var suggestions = scored.Take(MaxSuggestions)
            .Select(x => new BiteBotSuggestion(CatalogService.ToCard(x.Product), Reason(x.Product, intent, history)))
            .ToList();

        var combo = intent.Budget is not null || intent.People > 1 ? BuildCombo(scored.Select(s => s.Product).ToList(), intent) : null;

        return new BiteBotResponse(
            ComposeReply(intent, suggestions, combo, relaxed, history.HasHistory),
            suggestions, combo, Describe(intent), FollowUps(intent), engine);
    }

    private async Task<(FoodIntent Intent, string Engine)> InterpretAsync(string message, IReadOnlyList<string> categories, CancellationToken ct)
    {
        foreach (var extractor in extractors.Where(e => e.IsEnabled))
        {
            try
            {
                var intent = await extractor.ExtractAsync(message, categories, ct);
                if (intent is not null) return (intent.Sanitize(categories), extractor.ProviderName);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Never log the customer's message itself, only that the provider failed.
                logger.LogWarning(ex, "BiteBot intent provider {Provider} failed; falling back to rules", extractor.ProviderName);
            }
        }
        return (RuleBasedIntentParser.Parse(message).Sanitize(categories), "rules");
    }

    private sealed record History(HashSet<int> ProductIds, HashSet<int> CategoryIds, List<int> LastOrderProductIds, bool HasHistory);

    private async Task<History> LoadHistoryAsync(CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId) return new History([], [], [], false);

        var recent = await db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == userId && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.PaymentFailed && o.Status != OrderStatus.AwaitingPayment)
            .OrderByDescending(o => o.PlacedAt).Take(10).Select(o => o.Id).ToListAsync(ct);
        if (recent.Count == 0) return new History([], [], [], false);

        var items = await db.OrderItems.AsNoTracking().Where(i => recent.Contains(i.OrderId))
            .Select(i => new { i.OrderId, i.ProductId }).ToListAsync(ct);
        var productIds = items.Select(i => i.ProductId).ToHashSet();
        var categoryIds = (await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).Select(p => p.CategoryId).ToListAsync(ct)).ToHashSet();
        var last = items.Where(i => i.OrderId == recent[0]).Select(i => i.ProductId).ToList();
        return new History(productIds, categoryIds, last, true);
    }

    /// <summary>Applies hard constraints; if nothing survives, relaxes soft ones (category, then spice) and says so.</summary>
    private static (List<Product> Items, string? Relaxed) Filter(List<Product> all, FoodIntent intent)
    {
        IEnumerable<Product> Apply(bool useCategory, bool useSpice)
        {
            var q = all.AsEnumerable();
            if (intent.Vegetarian is { } veg) q = q.Where(p => p.IsVegetarian == veg);
            if (intent.Budget is { } budget) q = q.Where(p => p.Price <= budget);
            if (useSpice && intent.MinSpice is { } min) q = q.Where(p => p.SpiceLevel >= min);
            if (useSpice && intent.MaxSpice is { } max) q = q.Where(p => p.SpiceLevel <= max);
            if (useCategory && intent.Categories.Count > 0) q = q.Where(p => intent.Categories.Contains(p.Category!.Slug));
            return q;
        }

        var strict = Apply(true, true).ToList();
        if (strict.Count > 0) return (strict, null);
        var noCategory = Apply(false, true).ToList();
        if (noCategory.Count > 0) return (noCategory, "category");
        var noSpice = Apply(false, false).ToList();
        return (noSpice, noSpice.Count > 0 ? "spice" : "all");
    }

    private static double Score(Product p, FoodIntent intent, History history, IReadOnlyDictionary<int, int> popularity)
    {
        var tags = CatalogService.SplitTags(p.Tags);
        double score = 0;

        foreach (var k in intent.Keywords)
        {
            if (p.Name.Contains(k, StringComparison.OrdinalIgnoreCase)) score += 4;
            else if (tags.Any(t => t.Contains(k, StringComparison.OrdinalIgnoreCase))) score += 3;
            else if (p.Description.Contains(k, StringComparison.OrdinalIgnoreCase)) score += 1.5;
        }

        if (intent.Categories.Contains(p.Category!.Slug)) score += 3;
        if (intent.Healthy)
        {
            score += tags.Count(t => HealthyTags.Contains(t)) * 2.5;
            score -= tags.Count(t => IndulgentTags.Contains(t)) * 2;
        }
        if (intent.MealTime is { } meal && tags.Contains(meal)) score += 2;
        if (intent.MinSpice is not null) score += (int)p.SpiceLevel * 0.8;

        // Main dishes are better answers to "what should I eat" than drinks, unless the user asked for sides.
        if (intent.Categories.Count == 0 && SideCategories.Contains(p.Category.Slug)) score -= 2;

        if (intent.People > 1 && p.ServesCount > 1) score += 1.5;
        if (intent.Budget is { } budget) score += (double)(p.Price / budget) * 1.5; // prefer using the budget well, not the cheapest item

        if (history.HasHistory)
        {
            if (intent.SimilarToPastOrders)
            {
                if (history.LastOrderProductIds.Contains(p.Id)) score += 1; // familiar…
                else if (history.CategoryIds.Contains(p.CategoryId)) score += 4; // …but mostly "similar", not identical
            }
            else if (history.CategoryIds.Contains(p.CategoryId)) score += 0.75;
        }

        score += (double)p.AverageRating * 0.6;
        score += Math.Log10(1 + popularity.GetValueOrDefault(p.Id)) * 1.2;
        if (p.IsFeatured) score += 0.5;
        return score;
    }

    private static string Reason(Product p, FoodIntent intent, History history)
    {
        var parts = new List<string>();
        var tags = CatalogService.SplitTags(p.Tags);
        if (intent.SimilarToPastOrders && history.CategoryIds.Contains(p.CategoryId) && !history.LastOrderProductIds.Contains(p.Id))
            parts.Add($"similar to what you usually order from {p.Category!.Name}");
        if (intent.Healthy && tags.Any(t => HealthyTags.Contains(t))) parts.Add("a lighter, wholesome pick");
        if (intent.MinSpice is not null && p.SpiceLevel >= SpiceLevel.Medium) parts.Add(p.SpiceLevel == SpiceLevel.Hot ? "properly fiery" : "nicely spicy");
        if (intent.MaxSpice is not null && p.SpiceLevel <= SpiceLevel.Mild) parts.Add("mild on spice");
        if (p.ServesCount > 1) parts.Add($"serves {p.ServesCount}");
        if (p.ReviewCount > 0 && p.AverageRating >= 4.3m) parts.Add($"rated {p.AverageRating:0.0}★");
        if (parts.Count == 0) parts.Add(p.IsFeatured ? "a BiteBox favourite" : $"popular in {p.Category!.Name}");
        return char.ToUpperInvariant(parts[0][0]) + string.Join(", ", parts)[1..] + ".";
    }

    /// <summary>
    /// Greedy combo builder: enough mains to feed everyone, then a side/drink/dessert if budget allows.
    /// The total is the food subtotal before tax and delivery, which the reply states explicitly.
    /// </summary>
    private static BiteBotCombo? BuildCombo(List<Product> ranked, FoodIntent intent)
    {
        var budget = intent.Budget ?? decimal.MaxValue;
        var people = intent.People;
        var mains = ranked.Where(p => !SideCategories.Contains(p.Category!.Slug)).ToList();
        var sides = ranked.Where(p => SideCategories.Contains(p.Category!.Slug)).ToList();
        var picked = new Dictionary<int, (Product Product, int Qty)>();
        decimal total = 0;
        var servings = 0;

        // Vary mains: take the best-ranked main, then the next, cycling until everyone is fed.
        for (var guard = 0; servings < people && guard < 20; guard++)
        {
            var next = mains.Where(m => total + m.Price <= budget)
                .OrderBy(m => picked.TryGetValue(m.Id, out var e) ? e.Qty : 0)
                .ThenBy(m => mains.IndexOf(m))
                .FirstOrDefault();
            if (next is null) break;
            picked[next.Id] = (next, picked.TryGetValue(next.Id, out var cur) ? cur.Qty + 1 : 1);
            total += next.Price;
            servings += next.ServesCount;
        }

        if (servings == 0) return null;

        foreach (var side in sides.Where(s => !picked.ContainsKey(s.Id)).Take(2))
        {
            var qty = side.Category!.Slug == "beverages" ? people : 1;
            if (total + side.Price * qty > budget) continue;
            picked[side.Id] = (side, qty);
            total += side.Price * qty;
        }

        var fedAll = servings >= people;
        var summary = fedAll
            ? $"Feeds {people} for ₹{total:0} (food total, before taxes & delivery)."
            : $"Best I could fit in ₹{budget:0}: about {servings} serving(s) for ₹{total:0}.";

        return new BiteBotCombo(picked.Values.Select(v => new BiteBotComboItem(CatalogService.ToCard(v.Product), v.Qty)).ToList(), total, summary);
    }

    private static string ComposeReply(FoodIntent intent, IReadOnlyList<BiteBotSuggestion> suggestions, BiteBotCombo? combo, string? relaxed, bool hasHistory)
    {
        if (suggestions.Count == 0)
            return intent.Budget is { } b
                ? $"I couldn't find anything available under ₹{b:0} right now. Try a slightly higher budget, or ask me for snacks and drinks."
                : "I couldn't find a match on today's menu. Try something like \"spicy veg dinner under ₹300\".";

        var sb = new StringBuilder();
        var descriptor = new List<string>();
        if (intent.Vegetarian == true) descriptor.Add("vegetarian");
        if (intent.Vegetarian == false) descriptor.Add("non-veg");
        if (intent.MinSpice is not null) descriptor.Add("spicy");
        if (intent.MaxSpice is not null) descriptor.Add("mild");
        if (intent.Healthy) descriptor.Add("healthy");

        sb.Append(intent.SimilarToPastOrders && hasHistory ? "Based on your past orders, here are " : "Here are ");
        sb.Append(suggestions.Count == 1 ? "my top pick" : $"{suggestions.Count} {string.Join(" ", descriptor)} picks".Replace("  ", " "));
        if (intent.MealTime is { } meal) sb.Append($" for {meal.Replace("-", " ")}");
        if (intent.Budget is { } budget) sb.Append($" under ₹{budget:0}");
        if (intent.People > 1) sb.Append($" for {intent.People} people");
        sb.Append('.');

        if (intent.SimilarToPastOrders && !hasHistory) sb.Append(" I don't see any past orders yet, so I went with crowd favourites.");
        if (relaxed == "category") sb.Append(" Nothing in that exact category matched, so I widened the search.");
        if (relaxed == "spice") sb.Append(" I couldn't match the spice level exactly, so these are the closest.");
        if (combo is not null) sb.Append($" I've also put together a combo: {combo.Summary}");
        return sb.ToString();
    }

    private static IReadOnlyList<string> Describe(FoodIntent i)
    {
        var chips = new List<string>();
        if (i.Budget is { } b) chips.Add($"Under ₹{b:0}");
        if (i.People > 1) chips.Add($"For {i.People}");
        if (i.Vegetarian == true) chips.Add("Veg");
        if (i.Vegetarian == false) chips.Add("Non-veg");
        if (i.MinSpice is { } min) chips.Add(min == SpiceLevel.Hot ? "Extra spicy" : "Spicy");
        if (i.MaxSpice is not null) chips.Add("Mild");
        if (i.Healthy) chips.Add("Healthy");
        if (i.MealTime is { } m) chips.Add(char.ToUpperInvariant(m[0]) + m[1..].Replace("-", " "));
        if (i.SimilarToPastOrders) chips.Add("Like my past orders");
        chips.AddRange(i.Categories.Select(c => c.Replace("-", " & ")));
        chips.AddRange(i.Keywords.Take(3).Select(k => $"\"{k}\""));
        return chips;
    }

    private static IReadOnlyList<string> FollowUps(FoodIntent i)
    {
        var list = new List<string>();
        if (i.Vegetarian is null) list.Add("Only vegetarian please");
        if (i.Budget is null) list.Add("Something under ₹250");
        if (i.MinSpice is null) list.Add("Make it spicy");
        if (!i.Categories.Contains("desserts")) list.Add("Add a dessert");
        if (!i.SimilarToPastOrders) list.Add("Similar to my last order");
        return list.Take(3).ToList();
    }
}
