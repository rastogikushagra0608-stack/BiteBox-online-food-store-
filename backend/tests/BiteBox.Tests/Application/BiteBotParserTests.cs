using BiteBox.Application.Features.AI;
using BiteBox.Domain.Enums;

namespace BiteBox.Tests.Application;

public class BiteBotParserTests
{
    private static readonly string[] Categories =
        ["starters", "curries", "biryani-rice", "south-indian", "chinese", "pizzas-pasta", "burgers", "bowls-salads", "desserts", "beverages"];

    [Fact]
    public void Parses_budget_spice_and_meal()
    {
        var intent = RuleBasedIntentParser.Parse("Suggest a spicy meal under ₹250 for dinner");

        Assert.Equal(250m, intent.Budget);
        Assert.Equal(SpiceLevel.Medium, intent.MinSpice);
        Assert.Equal("dinner", intent.MealTime);
        Assert.Equal(1, intent.People);
    }

    [Fact]
    public void Parses_vegetarian_for_two()
    {
        var intent = RuleBasedIntentParser.Parse("I want vegetarian food for two");
        Assert.True(intent.Vegetarian);
        Assert.Equal(2, intent.People);
    }

    [Fact]
    public void Non_veg_words_win_over_veg_substring()
    {
        Assert.False(RuleBasedIntentParser.Parse("non-veg biryani please").Vegetarian);
        Assert.False(RuleBasedIntentParser.Parse("chicken something").Vegetarian);
    }

    [Fact]
    public void Healthy_and_affordable_sets_healthy_flag()
    {
        var intent = RuleBasedIntentParser.Parse("I want something healthy and affordable");
        Assert.True(intent.Healthy);
        Assert.Null(intent.Budget);
    }

    [Fact]
    public void Detects_similar_to_last_order()
    {
        Assert.True(RuleBasedIntentParser.Parse("Suggest something similar to my last order").SimilarToPastOrders);
    }

    [Theory]
    [InlineData("pizza and a shake", new[] { "pizzas-pasta", "beverages" })]
    [InlineData("masala dosa for breakfast", new[] { "south-indian" })]
    [InlineData("chocolate dessert", new[] { "desserts" })]
    public void Maps_food_words_to_categories(string message, string[] expected)
    {
        var intent = RuleBasedIntentParser.Parse(message).Sanitize(Categories);
        Assert.Equal(expected.OrderBy(x => x), intent.Categories.OrderBy(x => x));
    }

    [Theory]
    [InlineData("budget 400 family dinner", 400, 4)]
    [InlineData("rs 300 for 3 people", 300, 3)]
    [InlineData("something mild for kids", null, 1)]
    public void Parses_budget_and_people_variants(string message, int? budget, int people)
    {
        var intent = RuleBasedIntentParser.Parse(message);
        Assert.Equal(budget, (int?)intent.Budget);
        Assert.Equal(people, intent.People);
    }

    [Fact]
    public void Mild_request_sets_max_spice()
    {
        Assert.Equal(SpiceLevel.Mild, RuleBasedIntentParser.Parse("not spicy please").MaxSpice);
    }

    [Fact]
    public void Sanitize_drops_unknown_categories_and_clamps_values()
    {
        var raw = new FoodIntent { Budget = -5, People = 99, Categories = ["pizzas-pasta", "made-up"], Keywords = ["ok", "paneer", "DROP TABLE;"] };
        var clean = raw.Sanitize(Categories);

        Assert.Null(clean.Budget);
        Assert.Equal(20, clean.People);
        Assert.Equal(["pizzas-pasta"], clean.Categories);
        Assert.Equal(["paneer"], clean.Keywords);
    }
}
