using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FashionApp.Api.Infrastructure;

/// <summary>
/// Persists every <see cref="DateTimeOffset"/> as its UTC ticks (a sortable INTEGER).
/// Reading reconstructs a UTC value, so a write made with a non-UTC offset can never
/// break ordering or keyset pagination.
/// </summary>
public sealed class UtcTicksDateTimeOffsetConverter : ValueConverter<DateTimeOffset, long>
{
    public UtcTicksDateTimeOffsetConverter()
        : base(
            value => value.UtcTicks,
            ticks => new DateTimeOffset(ticks, TimeSpan.Zero))
    {
    }
}
