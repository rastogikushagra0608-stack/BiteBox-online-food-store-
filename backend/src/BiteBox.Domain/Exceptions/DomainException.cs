namespace BiteBox.Domain.Exceptions;

/// <summary>
/// Thrown when a business rule is violated. The <see cref="Code"/> is a stable, machine-readable
/// identifier that the API maps to an error response (e.g. "order.invalid_transition").
/// </summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
