using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Dispatch.Infrastructure.Configurations;

/// <summary>
/// SQL Server <c>datetime2</c> carries no time zone, so EF reads values back as
/// <see cref="DateTimeKind.Unspecified"/> and the API would serialise them without a "Z".
/// Everything stored is UTC; this stamps that on the way out.
/// </summary>
internal sealed class UtcDateTimeConverter()
    : ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
