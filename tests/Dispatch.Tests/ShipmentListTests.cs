using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dispatch.Application.Shipments;
using Dispatch.Domain;
using Microsoft.EntityFrameworkCore;

namespace Dispatch.Tests;

/// <summary>
/// GET /api/shipments through the real HTTP pipeline. Every test inserts its own rows and narrows the
/// request to them (a unique driver, a unique search token, or a date window no other test uses), so
/// no assertion depends on rows other tests inserted.
/// </summary>
[Collection(TestDatabaseCollection.Name)]
public sealed class ShipmentListTests(TestDatabaseFixture database) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    // Full-text population is asynchronous (CHANGE_TRACKING AUTO), so a search only sees new rows
    // after a short delay. Search assertions wait for the test's own rows first, bounded by this.
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(30);

    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(database.ConnectionString);
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Default_page_size_is_25_and_oversized_pageSize_is_clamped_to_100()
    {
        var driver = await TestData.CreateDriverAsync(database);
        var start = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        await TestData.CreateListShipmentsAsync(database, Enumerable.Range(0, 30).Select(i =>
            new ListRow(ShipmentStatus.PickedUp, start.AddMinutes(i), UniqueName("Paging Customer"), driver.Id)));

        var page = await GetPageAsync($"driverId={driver.Id}");

        Assert.Equal(1, page.Page);
        Assert.Equal(25, page.PageSize);
        Assert.Equal(25, page.Items.Count);
        Assert.Equal(30, page.TotalCount);

        var clamped = await GetPageAsync($"driverId={driver.Id}&pageSize=200");

        Assert.Equal(100, clamped.PageSize);
        Assert.Equal(30, clamped.Items.Count);
        Assert.Equal(30, clamped.TotalCount);
    }

    [Theory]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("pageSize=-1", "pageSize")]
    [InlineData("page=0", "page")]
    [InlineData("page=abc", "page")] // a binding failure has the same shape
    [InlineData("page=2147483647&pageSize=100", "page")] // row offset overflows int
    [InlineData("sortBy=weightKg", "sortBy")]
    [InlineData("sortDir=sideways", "sortDir")]
    [InlineData("status=Lost", "status")]
    [InlineData("status=1", "status")] // numbers are not status names
    [InlineData("createdFrom=2026-02-01T00:00:00Z&createdTo=2026-01-01T00:00:00Z", "createdFrom")]
    [InlineData("createdFrom=2026-01-01T00:00:00", "createdFrom")] // no offset: not an instant
    [InlineData("createdTo=2026-01-01", "createdTo")] // date only: not an instant
    public async Task Invalid_parameter_returns_400_problem_details_naming_it(string query, string parameter)
    {
        var response = await _client.GetAsync($"/api/shipments?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.True(problem.GetProperty("errors").TryGetProperty(parameter, out _), $"errors should name '{parameter}'.");
    }

    [Fact]
    public async Task Search_over_100_characters_returns_400_and_100_is_accepted()
    {
        var tooLong = await _client.GetAsync($"/api/shipments?search={new string('a', 101)}");
        var atCap = await _client.GetAsync($"/api/shipments?search={new string('a', 100)}");

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.True((await ReadProblemAsync(tooLong)).GetProperty("errors").TryGetProperty("search", out _));
        Assert.Equal(HttpStatusCode.OK, atCap.StatusCode);
    }

    [Fact]
    public async Task Driver_and_date_filters_work_alone_combine_with_AND_and_the_range_is_half_open()
    {
        // 2090 is used by no other test, so the date-only request sees only these rows.
        var from = new DateTime(2090, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(1);
        var driver = await TestData.CreateDriverAsync(database);
        var otherDriver = await TestData.CreateDriverAsync(database);
        var customer = UniqueName("Filter Customer");

        var rows = await TestData.CreateListShipmentsAsync(database,
        [
            new ListRow(ShipmentStatus.PickedUp, from, customer, driver.Id), // on the inclusive bound
            new ListRow(ShipmentStatus.PickedUp, to, customer, driver.Id), // on the exclusive bound
            new ListRow(ShipmentStatus.InTransit, from.AddHours(6), customer, driver.Id),
            new ListRow(ShipmentStatus.PickedUp, from.AddSeconds(-1), customer, driver.Id),
            new ListRow(ShipmentStatus.PickedUp, from.AddHours(1), customer, otherDriver.Id),
        ]);
        var (onFrom, onTo, wrongStatus, beforeFrom, wrongDriver) = (rows[0].Id, rows[1].Id, rows[2].Id, rows[3].Id, rows[4].Id);
        const string range = "createdFrom=2090-01-01T00:00:00Z&createdTo=2090-01-02T00:00:00Z";

        AssertIds([onFrom, onTo, wrongStatus, beforeFrom], await GetPageAsync($"driverId={driver.Id}"));
        AssertIds([onFrom, wrongStatus, wrongDriver], await GetPageAsync(range));

        // The same instants written with a +02:00 offset ('+' must be sent as %2B in a query string).
        AssertIds([onFrom, wrongStatus, wrongDriver],
            await GetPageAsync("createdFrom=2090-01-01T02:00:00%2B02:00&createdTo=2090-01-02T02:00:00%2B02:00"));

        AssertIds([onFrom], await GetPageAsync($"driverId={driver.Id}&status=PickedUp&{range}"));
    }

    [Fact]
    public async Task Status_filter_alone_returns_only_that_status()
    {
        // Newer than any other test's rows, so it is first in the default (createdAt desc) order.
        var driver = await TestData.CreateDriverAsync(database);
        var returned = (await TestData.CreateListShipmentsAsync(database,
        [
            new ListRow(ShipmentStatus.Returned, new DateTime(2099, 12, 31, 0, 0, 0, DateTimeKind.Utc), UniqueName("Status Customer"), driver.Id),
        ])).Single();

        var page = await GetPageAsync("status=returned&pageSize=100"); // names are case-insensitive

        Assert.Equal(returned.Id, page.Items[0].Id);
        Assert.All(page.Items, item => Assert.Equal(ShipmentStatus.Returned, item.Status));
        await using var db = database.CreateDbContext();
        Assert.Equal(await db.Shipments.CountAsync(s => s.Status == ShipmentStatus.Returned), page.TotalCount);
    }

    [Fact]
    public async Task Search_matches_a_word_prefix_but_not_a_mid_word_fragment()
    {
        var token = NewToken();
        var shipment = (await TestData.CreateListShipmentsAsync(database,
            [new ListRow(ShipmentStatus.Created, SearchRowTime, $"Prefix Customer {token}", null)])).Single();

        var byPrefix = await WaitForSearchAsync(token[..6], [shipment.Id]);
        Assert.Equal(1, byPrefix.TotalCount);

        // The row is indexed now (the prefix search found it), so an empty result is a real "no match".
        var byFragment = await GetPageAsync($"search={token[3..9]}");
        Assert.Equal(0, byFragment.TotalCount);
        Assert.Empty(byFragment.Items);
    }

    [Fact]
    public async Task Search_terms_combine_with_AND_in_any_order()
    {
        var (a, b) = (NewToken(), NewToken());
        var rows = await TestData.CreateListShipmentsAsync(database,
        [
            new ListRow(ShipmentStatus.Created, SearchRowTime, $"{a} {b}", null),
            new ListRow(ShipmentStatus.Created, SearchRowTime, $"{a} Other", null),
        ]);
        var (both, onlyA) = (rows[0].Id, rows[1].Id);

        await WaitForSearchAsync(a, [both, onlyA]);

        AssertIds([both], await GetPageAsync($"search={Uri.EscapeDataString($"{a} {b}")}"));
        AssertIds([both], await GetPageAsync($"search={Uri.EscapeDataString($"{b[..5]} {a[..5]}")}"));
    }

    [Fact]
    public async Task Search_keywords_are_ordinary_terms()
    {
        var token = NewToken();
        var shipment = (await TestData.CreateListShipmentsAsync(database,
            [new ListRow(ShipmentStatus.Created, SearchRowTime, $"{token} and or not", null)])).Single();
        await WaitForSearchAsync(token, [shipment.Id]);

        // Found only because AND, OR and NOT are searched for as words (and the stoplist is off).
        AssertIds([shipment.Id], await GetPageAsync($"search={Uri.EscapeDataString($"{token} AND OR NOT")}"));

        // "OR" is a term, not an operator: the row has no word starting with "zzzz", so nothing matches.
        AssertIds([], await GetPageAsync($"search={Uri.EscapeDataString($"\"{token}\" OR \"zzzz\"")}"));
    }

    [Theory]
    [InlineData("\"")]
    [InlineData("\"unterminated")]
    [InlineData("a\" OR \"b")]
    [InlineData("AND")]
    [InlineData("OR NOT")]
    [InlineData("NEAR(a, b)")]
    [InlineData("FORMSOF(INFLECTIONAL, run)")]
    [InlineData("ISABOUT(a WEIGHT(.5))")]
    [InlineData("a*")]
    [InlineData("~ & | !")]
    [InlineData("'; DROP TABLE Shipments; --")]
    [InlineData("[x] %_ @p")]
    public async Task Search_with_quotes_operators_or_syntax_returns_200(string search)
    {
        var response = await _client.GetAsync($"/api/shipments?search={Uri.EscapeDataString(search)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Search_with_punctuation_only_returns_an_empty_page()
    {
        var page = await GetPageAsync($"search={Uri.EscapeDataString("--**\"\"()!")}");

        Assert.Equal(0, page.TotalCount);
        Assert.Empty(page.Items);
        Assert.Equal(1, page.Page);
        Assert.Equal(25, page.PageSize);
    }

    [Fact]
    public async Task Paging_walk_over_rows_with_equal_sort_keys_visits_each_row_once_in_a_stable_order()
    {
        // Same status and same createdAt: sortBy=status ties on every row, so only the Id tie-breaker orders them.
        var driver = await TestData.CreateDriverAsync(database);
        var createdAt = new DateTime(2092, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var shipments = await TestData.CreateListShipmentsAsync(database, Enumerable.Range(0, 60).Select(_ =>
            new ListRow(ShipmentStatus.InTransit, createdAt, UniqueName("Walk Customer"), driver.Id)));

        var query = $"driverId={driver.Id}&sortBy=status&pageSize=7";
        var first = await WalkAsync(query);
        var second = await WalkAsync(query);

        Assert.Equal(60, first.TotalCount);
        Assert.Equal(first.Ids.Count, first.Ids.Distinct().Count()); // no id twice
        Assert.Equal(first.TotalCount, first.Ids.Distinct().Count());
        Assert.Equal(shipments.Select(s => s.Id).Order(), first.Ids.Order());
        Assert.Equal(first.Ids, second.Ids);

        // The tie-breaker runs in the sort's direction (desc by default).
        Assert.Equal(first.Ids.OrderByDescending(id => id), first.Ids);
    }

    [Fact]
    public async Task TotalCount_equals_the_number_of_items_a_full_walk_returns()
    {
        // Through full-text search, so the count and page queries both carry CONTAINS.
        var token = NewToken();
        var start = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var shipments = await TestData.CreateListShipmentsAsync(database, Enumerable.Range(0, 12).Select(i =>
            new ListRow(ShipmentStatus.Created, start.AddMinutes(i), $"Count Customer {token}", null)));
        await WaitForSearchAsync(token, shipments.Select(s => s.Id).ToList());

        var walk = await WalkAsync($"search={token}&pageSize=5");

        Assert.Equal(12, walk.TotalCount);
        Assert.Equal(walk.TotalCount, walk.Ids.Count);
    }

    [Fact]
    public async Task Items_carry_customer_name_and_driver_name_which_is_null_when_unassigned()
    {
        // 2091 is used by no other test.
        var createdAt = new DateTime(2091, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var driver = await TestData.CreateDriverAsync(database);
        var customer = UniqueName("Fields Customer");
        var rows = await TestData.CreateListShipmentsAsync(database,
        [
            new ListRow(ShipmentStatus.Created, createdAt, customer, null, "Port Said"),
            new ListRow(ShipmentStatus.PickedUp, createdAt.AddHours(1), customer, driver.Id, "Port Said"),
        ]);

        var page = await GetPageAsync("createdFrom=2091-01-01T00:00:00Z&createdTo=2091-01-02T00:00:00Z&sortBy=createdAt&sortDir=asc");

        Assert.Equal(rows.Select(r => r.Id), page.Items.Select(i => i.Id));
        var (unassigned, assigned) = (page.Items[0], page.Items[1]);

        Assert.Equal(rows[0].TrackingNumber, unassigned.TrackingNumber);
        Assert.Equal(rows[0].CustomerId, unassigned.CustomerId);
        Assert.Equal(customer, unassigned.CustomerName);
        Assert.Null(unassigned.DriverId);
        Assert.Null(unassigned.DriverName);
        Assert.Equal("Central Hub", unassigned.Origin);
        Assert.Equal("Port Said", unassigned.Destination);
        Assert.Equal(1.5m, unassigned.WeightKg);
        Assert.Equal(ShipmentStatus.Created, unassigned.Status);
        Assert.Equal(createdAt, unassigned.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, unassigned.CreatedAt.Kind);
        Assert.Equal(createdAt, unassigned.UpdatedAt);
        Assert.Equal(0, unassigned.Version);

        Assert.Equal(customer, assigned.CustomerName);
        Assert.Equal(driver.Id, assigned.DriverId);
        Assert.Equal(driver.Name, assigned.DriverName);
        Assert.Equal(ShipmentStatus.PickedUp, assigned.Status);
        Assert.Equal(1, assigned.Version);
    }

    [Fact]
    public async Task List_request_issues_exactly_two_sql_commands_count_and_page()
    {
        var token = NewToken();
        var driver = await TestData.CreateDriverAsync(database);
        var start = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var shipments = await TestData.CreateListShipmentsAsync(database, Enumerable.Range(0, 5).Select(i =>
            new ListRow(ShipmentStatus.PickedUp, start.AddMinutes(i), $"{token} Customer {i}", driver.Id)));
        await WaitForSearchAsync(token, shipments.Select(s => s.Id).ToList());

        var counter = new CommandCountingInterceptor();
        await using var factory = new ApiFactory(database.ConnectionString, null, counter);
        using var client = factory.CreateClient();

        // Every filter and the search, sorted through the customer join: rows come back with both names,
        // so an N+1 would show up as extra commands here.
        var response = await client.GetAsync(
            $"/api/shipments?driverId={driver.Id}&status=PickedUp&search={token}" +
            "&createdFrom=2026-05-01T00:00:00Z&createdTo=2026-05-02T00:00:00Z&sortBy=customerName");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<ShipmentPageDto>(Json);
        Assert.NotNull(page);
        Assert.Equal(5, page.Items.Count);
        Assert.All(page.Items, item => Assert.Equal(driver.Name, item.DriverName));

        Assert.Equal(2, counter.Commands.Count);
        Assert.Single(counter.Commands, sql => sql.Contains("COUNT(*)"));
        Assert.Single(counter.Commands, sql => sql.Contains("OFFSET"));
    }

    /// <summary>Old enough not to compete with the date-window tests' rows; search tests filter by token.</summary>
    private static readonly DateTime SearchRowTime = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>A word no other row contains: "zq" and ten random letters (letters only, so it is one full-text word).</summary>
    private static string NewToken() =>
        "zq" + string.Concat(Guid.NewGuid().ToByteArray().Take(10).Select(b => (char)('a' + b % 26)));

    private static string UniqueName(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    private async Task<ShipmentPageDto> GetPageAsync(string query)
    {
        var response = await _client.GetAsync($"/api/shipments?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<ShipmentPageDto>(Json);
        Assert.NotNull(page);
        return page;
    }

    /// <summary>
    /// Polls this test's own search until it returns exactly <paramref name="expectedIds"/>, bounded by
    /// <see cref="SearchTimeout"/>. Use it before any search assertion: new rows reach the full-text index
    /// asynchronously, so an immediate search can miss them.
    /// </summary>
    private async Task<ShipmentPageDto> WaitForSearchAsync(string search, IReadOnlyCollection<int> expectedIds)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var page = await GetPageAsync($"search={Uri.EscapeDataString(search)}&pageSize=100");
            if (page.Items.Select(i => i.Id).Order().SequenceEqual(expectedIds.Order()))
            {
                return page;
            }

            if (stopwatch.Elapsed > SearchTimeout)
            {
                Assert.Fail(
                    $"Search '{search}' did not return the expected {expectedIds.Count} shipments within " +
                    $"{SearchTimeout.TotalSeconds} s; it returned {page.TotalCount}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
    }

    /// <summary>Requests every page in turn and returns the ids in the order they came back.</summary>
    private async Task<(int TotalCount, List<int> Ids)> WalkAsync(string query)
    {
        var ids = new List<int>();
        var first = await GetPageAsync($"{query}&page=1");
        var lastPage = (first.TotalCount + first.PageSize - 1) / first.PageSize;
        ids.AddRange(first.Items.Select(i => i.Id));

        for (var page = 2; page <= lastPage; page++)
        {
            var next = await GetPageAsync($"{query}&page={page}");
            Assert.Equal(first.TotalCount, next.TotalCount);
            ids.AddRange(next.Items.Select(i => i.Id));
        }

        return (first.TotalCount, ids);
    }

    private static void AssertIds(IEnumerable<int> expected, ShipmentPageDto page)
    {
        Assert.Equal(expected.Order(), page.Items.Select(i => i.Id).Order());
        Assert.Equal(page.Items.Count, page.TotalCount);
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
