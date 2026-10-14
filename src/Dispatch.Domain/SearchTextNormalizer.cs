namespace Dispatch.Domain;

/// <summary>
/// The one normalisation function for search text. It is used both when writing
/// <c>Shipment.SearchText</c> and when normalising a user's search term, so the two always agree.
/// </summary>
public static class SearchTextNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lowered = value.ToLowerInvariant();
        var builder = new System.Text.StringBuilder(lowered.Length);
        var pendingSpace = false;

        foreach (var c in lowered)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>Builds the denormalised search column: tracking number, customer name, destination.</summary>
    public static string Build(string trackingNumber, string customerName, string destination) =>
        Normalize($"{trackingNumber} {customerName} {destination}");
}
