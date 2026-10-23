using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TravelPlanner.Infrastructure.Persistence;

internal static class JsonColumn
{
    /// <summary>Stores a small object graph (e.g. a translations dictionary) as a jsonb column.</summary>
    public static PropertyBuilder<TValue> AsJsonb<TValue>(this PropertyBuilder<TValue> builder) where TValue : class, new() =>
        builder.HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<TValue>(v, (JsonSerializerOptions?)null) ?? new TValue(),
            new ValueComparer<TValue>(
                (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                v => JsonSerializer.Deserialize<TValue>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!));
}
