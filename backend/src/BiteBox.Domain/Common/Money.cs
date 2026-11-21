namespace BiteBox.Domain.Common;

/// <summary>Monetary helpers. All money is stored as decimal(10,2) in Indian Rupees.</summary>
public static class Money
{
    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
