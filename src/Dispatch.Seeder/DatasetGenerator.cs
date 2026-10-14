using Dispatch.Domain;
using Dispatch.Domain.Entities;

namespace Dispatch.Seeder;

internal sealed record SeedDataset(
    IReadOnlyList<Customer> Customers,
    IReadOnlyList<Driver> Drivers,
    IReadOnlyList<Shipment> Shipments,
    IReadOnlyList<ShipmentStatusHistory> History);

/// <summary>
/// Builds the whole dataset in memory. Fully deterministic: a fixed random seed and a fixed anchor
/// date (never the wall clock), so every machine and every run produces identical data.
/// </summary>
internal static class DatasetGenerator
{
    public const int Seed = 20261001;

    /// <summary>"Now" for the dataset. Shipment ages and status progress are measured against this.</summary>
    public static readonly DateTime Anchor = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    public const int CustomerCount = 5_000;
    public const int DriverCount = 200;
    public const int ShipmentCount = 50_000;

    private const int CreationWindowDays = 540;

    // Exponent > 1 skews creation times toward the anchor, which gives a realistic in-flight tail.
    private const double RecencySkew = 2.0;

    private const double FailureRate = 0.06;
    private const double ReturnRate = 0.05; // of delivered shipments, once enough time has passed

    private const int Hour = 3600;
    private const int Day = 24 * Hour;

    // Seconds between reaching the previous status and the one named here: (min, max).
    private static readonly Dictionary<ShipmentStatus, (int Min, int Max)> StepDurations = new()
    {
        [ShipmentStatus.PickedUp] = (1 * Hour, 24 * Hour),
        [ShipmentStatus.InTransit] = (4 * Hour, 72 * Hour),
        [ShipmentStatus.OutForDelivery] = (4 * Hour, 48 * Hour),
        [ShipmentStatus.Delivered] = (1 * Hour, 12 * Hour),
        [ShipmentStatus.Failed] = (1 * Hour, 12 * Hour),
        [ShipmentStatus.Returned] = (1 * Day, 10 * Day),
    };

    public static SeedDataset Generate()
    {
        var rng = new Random(Seed);

        var customers = GenerateCustomers(rng);
        var drivers = GenerateDrivers(rng);
        var (shipments, history) = GenerateShipments(rng, customers);

        return new SeedDataset(customers, drivers, shipments, history);
    }

    private static List<Customer> GenerateCustomers(Random rng)
    {
        var prefixes = SeedLists.CustomerPrefixes;
        var cores = SeedLists.CustomerCores;
        var suffixes = SeedLists.CustomerSuffixes;
        var picks = SampleDistinct(rng, CustomerCount, prefixes.Length * cores.Length * suffixes.Length);

        var customers = new List<Customer>(CustomerCount);
        for (var i = 0; i < picks.Length; i++)
        {
            var p = picks[i];
            var suffix = suffixes[p % suffixes.Length];
            var core = cores[p / suffixes.Length % cores.Length];
            var prefix = prefixes[p / (suffixes.Length * cores.Length)];
            customers.Add(new Customer { Id = i + 1, Name = $"{prefix} {core} {suffix}" });
        }

        return customers;
    }

    private static List<Driver> GenerateDrivers(Random rng)
    {
        var firsts = SeedLists.DriverFirstNames;
        var lasts = SeedLists.DriverLastNames;
        var picks = SampleDistinct(rng, DriverCount, firsts.Length * lasts.Length);

        var drivers = new List<Driver>(DriverCount);
        for (var i = 0; i < picks.Length; i++)
        {
            drivers.Add(new Driver { Id = i + 1, Name = $"{firsts[picks[i] / lasts.Length]} {lasts[picks[i] % lasts.Length]}" });
        }

        return drivers;
    }

    private static (List<Shipment> Shipments, List<ShipmentStatusHistory> History) GenerateShipments(
        Random rng, List<Customer> customers)
    {
        // Ids ascend with CreatedAt, so Id order is creation order.
        var createdTimes = new DateTime[ShipmentCount];
        for (var i = 0; i < createdTimes.Length; i++)
        {
            var ageSeconds = (long)(CreationWindowDays * (double)Day * Math.Pow(rng.NextDouble(), RecencySkew));
            createdTimes[i] = Anchor.AddSeconds(-ageSeconds);
        }

        Array.Sort(createdTimes);

        var shipments = new List<Shipment>(ShipmentCount);
        var history = new List<ShipmentStatusHistory>(ShipmentCount * 5);

        for (var i = 0; i < ShipmentCount; i++)
        {
            var shipmentId = i + 1;
            var createdAt = createdTimes[i];
            var customer = customers[rng.Next(customers.Count)];
            var origin = SeedLists.Origins[rng.Next(SeedLists.Origins.Length)];
            var destination = SeedLists.Destinations[rng.Next(SeedLists.Destinations.Length)];
            var weightKg = (decimal)Math.Round(0.1 + 49.9 * Math.Pow(rng.NextDouble(), 2), 2);
            var assignedDriverId = rng.Next(1, DriverCount + 1);
            var willFail = rng.NextDouble() < FailureRate;
            var willReturn = rng.NextDouble() < ReturnRate;

            history.Add(NewHistoryRow(history.Count + 1, shipmentId, null, ShipmentStatus.Created, createdAt));

            // Walk the transition map forward from Created, stopping when the next step would land
            // after the anchor. Old shipments are terminal because they had time to finish.
            var current = ShipmentStatus.Created;
            var changedAt = createdAt;
            var transitions = 0;

            while (ChooseNext(current, willFail, willReturn) is { } next)
            {
                var (min, max) = StepDurations[next];
                var nextAt = changedAt.AddSeconds(rng.Next(min, max + 1));
                if (nextAt > Anchor)
                {
                    break;
                }

                history.Add(NewHistoryRow(history.Count + 1, shipmentId, current, next, nextAt));
                current = next;
                changedAt = nextAt;
                transitions++;
            }

            var trackingNumber = TrackingNumberFor(shipmentId);
            shipments.Add(new Shipment
            {
                Id = shipmentId,
                TrackingNumber = trackingNumber,
                CustomerId = customer.Id,
                DriverId = current == ShipmentStatus.Created ? null : assignedDriverId,
                Origin = origin,
                Destination = destination,
                WeightKg = weightKg,
                Status = current,
                CreatedAt = createdAt,
                UpdatedAt = changedAt,
                Version = transitions,
                SearchText = SearchTextNormalizer.Build(trackingNumber, customer.Name, destination),
            });
        }

        return (shipments, history);
    }

    /// <summary>Format: DSP + 8 digits, no separators (a single full-text token).</summary>
    private static string TrackingNumberFor(int shipmentId) => $"DSP{10_000_000 + shipmentId}";

    /// <summary>
    /// The next status, drawn from the shared transition map. Returns null when the shipment's
    /// planned journey ends here (terminal state, or delivered and never returned).
    /// </summary>
    private static ShipmentStatus? ChooseNext(ShipmentStatus current, bool willFail, bool willReturn)
    {
        var allowed = ShipmentTransitions.AllowedFrom(current);
        if (allowed.Count == 0 || (current == ShipmentStatus.Delivered && !willReturn))
        {
            return null;
        }

        var next = allowed.Count == 1
            ? allowed[0]
            : willFail ? ShipmentStatus.Failed : ShipmentStatus.Delivered;

        // Guards the generator against drifting out of step with the transition map.
        if (!ShipmentTransitions.IsAllowed(current, next))
        {
            throw new InvalidOperationException($"Seed generator chose illegal transition {current} -> {next}.");
        }

        return next;
    }

    private static ShipmentStatusHistory NewHistoryRow(
        int id, int shipmentId, ShipmentStatus? from, ShipmentStatus to, DateTime changedAt) =>
        new() { Id = id, ShipmentId = shipmentId, FromStatus = from, ToStatus = to, ChangedAt = changedAt };

    /// <summary>Picks <paramref name="count"/> distinct integers in [0, total) with a partial Fisher-Yates shuffle.</summary>
    private static int[] SampleDistinct(Random rng, int count, int total)
    {
        if (count > total)
        {
            throw new InvalidOperationException($"Cannot draw {count} distinct names from {total} combinations.");
        }

        var pool = new int[total];
        for (var i = 0; i < total; i++)
        {
            pool[i] = i;
        }

        for (var i = 0; i < count; i++)
        {
            var j = i + rng.Next(total - i);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        return pool[..count];
    }
}
