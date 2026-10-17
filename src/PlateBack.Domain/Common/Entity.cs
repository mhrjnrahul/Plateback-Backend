namespace PlateBack.Domain.Common;

/// <summary>Base class for entities identified by a GUID.</summary>
public abstract class Entity
{
    public Guid Id { get; protected init; }
}
