namespace BiteBox.Domain.Common;

/// <summary>Marker for something important that happened in the domain (Observer pattern).</summary>
public interface IDomainEvent
{
    DateTime OccurredAt { get; }
}
