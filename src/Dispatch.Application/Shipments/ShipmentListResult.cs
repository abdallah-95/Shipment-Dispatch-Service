namespace Dispatch.Application.Shipments;

/// <summary>Every outcome of a list request. The controller maps each case to an HTTP response.</summary>
public abstract record ShipmentListResult
{
    private ShipmentListResult()
    {
    }

    public sealed record Success(ShipmentPageDto Page) : ShipmentListResult;

    /// <summary>One or more parameters are invalid. Keyed by query parameter name.</summary>
    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : ShipmentListResult;
}
