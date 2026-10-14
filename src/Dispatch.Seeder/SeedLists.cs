namespace Dispatch.Seeder;

/// <summary>
/// Source lists for generated names. Many entries deliberately share prefixes (Cedar/Cedarview,
/// Port Said/Port Louis, San Diego/San Jose, ...) so a word-prefix search returns meaningful pages.
/// </summary>
internal static class SeedLists
{
    public static readonly string[] CustomerPrefixes =
    [
        "Alpine", "Apex", "Arbor", "Atlas", "Aurora", "Beacon", "Birchwood", "Bluewater", "Bridgeport", "Cedar",
        "Cedarview", "Crescent", "Eastgate", "Eastwood", "Evergreen", "Fairview", "Frontier", "Granite", "Harbor",
        "Harborview", "Highland", "Ironwood", "Lakeside", "Lakeview", "Maple", "Meridian", "Northgate", "Northwind",
        "Oakridge", "Pinecrest",
    ];

    public static readonly string[] CustomerCores =
    [
        "Trading", "Supply", "Foods", "Textiles", "Electronics", "Pharma", "Hardware", "Furniture", "Books", "Garments",
        "Plastics", "Metals", "Tools", "Toys", "Optics", "Paper", "Print", "Ceramics", "Cosmetics", "Beverages",
        "Bakery", "Motors", "Parts", "Cycles", "Gardens", "Timber", "Glassworks", "Packaging", "Machinery", "Medical",
    ];

    public static readonly string[] CustomerSuffixes =
    [
        "Ltd", "LLC", "Inc", "Co", "Group", "Partners", "& Sons", "Holdings", "Corp", "GmbH", "SA", "Traders",
    ];

    public static readonly string[] DriverFirstNames =
    [
        "Adam", "Amara", "Ben", "Bianca", "Carlos", "Chloe", "Daniel", "Dina", "Elias", "Elena",
        "Farid", "Fiona", "Gabriel", "Grace", "Hassan", "Hana", "Ivan", "Irene", "Jamal", "Julia",
    ];

    public static readonly string[] DriverLastNames =
    [
        "Abbott", "Baker", "Castillo", "Dawson", "Ellis", "Fischer", "Garcia", "Hughes", "Ibrahim", "Jensen",
        "Kowalski", "Lopez", "Mendes", "Nguyen", "Okafor", "Petrov", "Quinn", "Rossi", "Silva", "Tanaka",
    ];

    public static readonly string[] Origins =
    [
        "Northgate Depot", "Southport Depot", "Eastside Depot", "Westfield Depot", "Central Hub", "Riverside Hub",
        "Airport Hub", "Harbor Hub", "Midtown Depot", "Hillcrest Depot", "Lakeside Depot", "Junction Hub",
    ];

    public static readonly string[] Destinations =
    [
        "Port Alfred", "Port Elizabeth", "Port Harcourt", "Port Louis", "Port Moresby", "Port Said",
        "San Antonio", "San Diego", "San Jose", "San Juan", "San Marino", "San Remo",
        "Santa Cruz", "Santa Fe", "Santa Monica", "Santa Rosa",
        "Saint Albans", "Saint Louis", "Saint Paul", "Saint Petersburg",
        "New Haven", "New Orleans", "New York", "Newcastle", "Newport", "Newark",
        "North Bay", "North Shields", "Northampton", "Norwich",
        "South Bend", "South Shields", "Southampton", "Southport",
        "East London", "East Lansing", "Eastbourne", "Easton",
        "West Palm Beach", "West Bromwich", "Westminster", "Weston",
        "Fort Collins", "Fort Lauderdale", "Fort Wayne", "Fort Worth",
        "Mount Pleasant", "Mount Vernon", "Mountain View",
        "Lake Charles", "Lake Placid", "Lakewood", "Lakeland",
        "Bristol", "Brighton", "Bradford", "Birmingham", "Bologna", "Bordeaux",
        "Cambridge", "Cardiff", "Carlisle", "Chester", "Chicago", "Cincinnati",
        "Dallas", "Denver", "Detroit", "Dublin", "Dundee", "Durham",
        "Edinburgh", "Exeter", "Frankfurt", "Geneva", "Glasgow", "Gloucester",
        "Hamburg", "Hartford", "Houston", "Hull", "Inverness", "Ipswich",
        "Leeds", "Leicester", "Lincoln", "Liverpool", "Lyon", "Madrid",
        "Manchester", "Marseille", "Milan", "Munich", "Nottingham", "Oxford",
        "Plymouth", "Portsmouth", "Preston", "Reading", "Sheffield", "Stockholm",
    ];
}
