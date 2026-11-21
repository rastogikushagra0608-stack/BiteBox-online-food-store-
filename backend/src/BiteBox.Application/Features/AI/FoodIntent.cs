using System.Globalization;
using System.Text.RegularExpressions;
using BiteBox.Domain.Enums;

namespace BiteBox.Application.Features.AI;

/// <summary>
/// Structured interpretation of a free-text food request. This is the ONLY thing a language model is
/// allowed to produce: it never chooses dishes, prices or availability. Those come from the database.
/// </summary>
public sealed record FoodIntent
{
    public decimal? Budget { get; init; }
    public int People { get; init; } = 1;
    public bool? Vegetarian { get; init; }
    public SpiceLevel? MinSpice { get; init; }
    public SpiceLevel? MaxSpice { get; init; }
    public bool Healthy { get; init; }
    public IReadOnlyList<string> Categories { get; init; } = [];
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public string? MealTime { get; init; }
    public bool SimilarToPastOrders { get; init; }

    public static readonly string[] MealTimes = ["breakfast", "lunch", "dinner", "snack", "late-night"];

    /// <summary>Clamps and sanitises an intent produced by an external model before it is trusted.</summary>
    public FoodIntent Sanitize(IReadOnlyCollection<string> knownCategories) => this with
    {
        Budget = Budget is > 0 and <= 100_000 ? Math.Round(Budget.Value, 0) : null,
        People = Math.Clamp(People, 1, 20),
        MinSpice = MinSpice is { } min && Enum.IsDefined(min) ? min : null,
        MaxSpice = MaxSpice is { } max && Enum.IsDefined(max) ? max : null,
        Categories = Categories.Select(c => c.Trim().ToLowerInvariant()).Where(knownCategories.Contains).Distinct().Take(4).ToList(),
        Keywords = Keywords.Select(k => k.Trim().ToLowerInvariant()).Where(k => k.Length is >= 3 and <= 30 && k.All(ch => char.IsLetter(ch) || ch == '-' || ch == ' '))
            .Distinct().Take(8).ToList(),
        MealTime = MealTime is not null && MealTimes.Contains(MealTime.ToLowerInvariant()) ? MealTime.ToLowerInvariant() : null
    };
}

/// <summary>
/// Optional language-model adapter that turns text into a <see cref="FoodIntent"/>. Returns null when
/// the provider is not configured, times out, or returns something unusable, in which case BiteBot
/// falls back to <see cref="RuleBasedIntentParser"/>. Implemented in Infrastructure (Adapter pattern).
/// </summary>
public interface IFoodIntentExtractor
{
    string ProviderName { get; }
    bool IsEnabled { get; }
    Task<FoodIntent?> ExtractAsync(string message, IReadOnlyList<string> categorySlugs, CancellationToken ct);
}

/// <summary>
/// Deterministic, offline intent parser. Always available, free, and fully unit-testable.
/// Handles the common phrasings ("spicy veg dinner under ₹300 for two").
/// </summary>
public static partial class RuleBasedIntentParser
{
    private static readonly Dictionary<string, string> CategorySynonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["starter"] = "starters", ["starters"] = "starters", ["snack"] = "starters", ["snacks"] = "starters", ["samosa"] = "starters",
        ["kebab"] = "starters", ["kebabs"] = "starters", ["tandoori"] = "starters", ["street"] = "starters",
        ["biryani"] = "biryani-rice", ["pulao"] = "biryani-rice",
        ["curry"] = "curries", ["curries"] = "curries", ["gravy"] = "curries", ["thali"] = "curries", ["roti"] = "curries", ["naan"] = "curries",
        ["dosa"] = "south-indian", ["idli"] = "south-indian", ["vada"] = "south-indian", ["sambar"] = "south-indian",
        ["pizza"] = "pizzas-pasta", ["pizzas"] = "pizzas-pasta", ["pasta"] = "pizzas-pasta", ["penne"] = "pizzas-pasta",
        ["burger"] = "burgers", ["burgers"] = "burgers", ["sandwich"] = "burgers", ["sandwiches"] = "burgers",
        ["bowl"] = "bowls-salads", ["bowls"] = "bowls-salads", ["salad"] = "bowls-salads", ["salads"] = "bowls-salads",
        ["chinese"] = "chinese", ["noodles"] = "chinese", ["momos"] = "chinese", ["momo"] = "chinese", ["hakka"] = "chinese",
        ["dessert"] = "desserts", ["desserts"] = "desserts", ["sweet"] = "desserts", ["sweets"] = "desserts", ["cake"] = "desserts",
        ["brownie"] = "desserts", ["ice-cream"] = "desserts", ["sundae"] = "desserts",
        ["drink"] = "beverages", ["drinks"] = "beverages", ["beverage"] = "beverages", ["beverages"] = "beverages", ["shake"] = "beverages",
        ["juice"] = "beverages", ["coffee"] = "beverages", ["mocktail"] = "beverages", ["thirsty"] = "beverages"
    };

    private static readonly Dictionary<string, int> NumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8
    };

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "want", "something", "suggest", "recommend", "give", "show", "some", "food", "meal", "dish", "dishes",
        "please", "can", "you", "like", "need", "would", "love", "get", "under", "below", "less", "than", "within", "upto", "budget",
        "people", "persons", "person", "of", "us", "me", "my", "our", "is", "are", "what", "good", "nice", "best", "tonight", "today",
        "order", "eat", "have", "any", "also", "but", "not", "very", "extra", "really", "rupees", "rs", "inr", "veg", "vegetarian",
        "non", "spicy", "hot", "mild", "healthy", "light", "dinner", "lunch", "breakfast", "snack", "family", "couple", "something",
        "similar", "last", "previous", "usual", "again", "time", "items", "item", "affordable", "cheap", "dinner", "late", "night", "too", "much", "options"
    };

    [GeneratedRegex(@"(?:under|below|less than|within|upto|up to|max(?:imum)?|budget(?: of| is)?|around|about)\s*(?:rs\.?|₹|inr)?\s*(\d{2,5})", RegexOptions.IgnoreCase)]
    private static partial Regex BudgetPhrase();

    [GeneratedRegex(@"(?:₹|rs\.?|inr)\s*(\d{2,5})|(\d{2,5})\s*(?:rs|rupees|inr|/-)", RegexOptions.IgnoreCase)]
    private static partial Regex CurrencyAmount();

    [GeneratedRegex(@"\bfor\s+(\d{1,2}|one|two|three|four|five|six|seven|eight)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PeoplePhrase();

    [GeneratedRegex(@"\b(non[\s-]?veg(?:etarian)?|chicken|mutton|fish|prawns?|egg|eggs|meat|keema|lamb)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NonVeg();

    [GeneratedRegex(@"\b(veg|vegetarian|vegan|no meat|paneer|plant[\s-]based)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Veg();

    [GeneratedRegex(@"\b(not spicy|non[\s-]?spicy|no spice|less spicy|mild|kids?|bland|light on spice)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Mild();

    [GeneratedRegex(@"\b(extra spicy|very spicy|super spicy|fiery|really hot)\b", RegexOptions.IgnoreCase)]
    private static partial Regex VerySpicy();

    [GeneratedRegex(@"\b(spicy|hot|chilli|chili|tangy|masaledar)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Spicy();

    [GeneratedRegex(@"\b(healthy|light|low[\s-]?cal(?:orie)?|diet|protein|fit|fitness|clean|nutritious|gym)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Healthy();

    [GeneratedRegex(@"\b(similar|same as|like my last|my usual|usual|again|previous order|last order|what i (?:had|ordered))\b", RegexOptions.IgnoreCase)]
    private static partial Regex Similar();

    [GeneratedRegex(@"[a-z][a-z\-]+", RegexOptions.IgnoreCase)]
    private static partial Regex Word();

    public static FoodIntent Parse(string message)
    {
        var text = message.Trim();
        var lower = text.ToLowerInvariant();

        decimal? budget = null;
        var b = BudgetPhrase().Match(text);
        if (b.Success) budget = decimal.Parse(b.Groups[1].Value, CultureInfo.InvariantCulture);
        else
        {
            var c = CurrencyAmount().Match(text);
            if (c.Success) budget = decimal.Parse(c.Groups[1].Success ? c.Groups[1].Value : c.Groups[2].Value, CultureInfo.InvariantCulture);
        }

        var people = 1;
        var p = PeoplePhrase().Match(text);
        if (p.Success) people = int.TryParse(p.Groups[1].Value, out var n) ? n : NumberWords[p.Groups[1].Value];
        else if (lower.Contains("family")) people = 4;
        else if (lower.Contains("couple") || lower.Contains("date night")) people = 2;

        bool? vegetarian = NonVeg().IsMatch(text) ? false : Veg().IsMatch(text) ? true : null;

        SpiceLevel? minSpice = null, maxSpice = null;
        if (Mild().IsMatch(text)) maxSpice = SpiceLevel.Mild;
        else if (VerySpicy().IsMatch(text)) minSpice = SpiceLevel.Hot;
        else if (Spicy().IsMatch(text)) minSpice = SpiceLevel.Medium;

        var words = Word().Matches(lower).Select(m => m.Value).ToList();
        var categories = words.Select(w => CategorySynonyms.GetValueOrDefault(w)).OfType<string>().Distinct().ToList();

        var mealTime = FoodIntent.MealTimes.FirstOrDefault(m => lower.Contains(m.Replace("-", " ")) || lower.Contains(m));
        if (mealTime is null && (lower.Contains("midnight") || lower.Contains("late night"))) mealTime = "late-night";

        var keywords = words
            .Where(w => w.Length >= 3 && !StopWords.Contains(w) && !CategorySynonyms.ContainsKey(w) && !NumberWords.ContainsKey(w))
            .Distinct().Take(8).ToList();

        return new FoodIntent
        {
            Budget = budget,
            People = Math.Clamp(people, 1, 20),
            Vegetarian = vegetarian,
            MinSpice = minSpice,
            MaxSpice = maxSpice,
            Healthy = Healthy().IsMatch(text),
            Categories = categories,
            Keywords = keywords,
            MealTime = mealTime,
            SimilarToPastOrders = Similar().IsMatch(text)
        };
    }
}
