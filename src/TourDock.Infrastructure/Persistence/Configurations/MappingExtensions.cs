using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Infrastructure.Persistence.Configurations;

internal static class MappingExtensions
{
    /// <summary>Maps a <see cref="Money"/> property to two columns: {prefix}_amount numeric(12,2) and {prefix}_currency char(3).</summary>
    public static void MapMoney<TEntity>(
        this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, Money?>> property, string prefix, bool required = true)
        where TEntity : class
    {
        var complex = builder.ComplexProperty(property!);
        complex.IsRequired(required);
        complex.Property(m => m.Amount).HasColumnName($"{prefix}_amount").HasPrecision(12, 2);
        complex.Property(m => m.Currency).HasColumnName($"{prefix}_currency").HasMaxLength(3).IsFixedLength();
    }

    /// <summary>Stores a <see cref="CancellationPolicy"/> as jsonb (an immutable snapshot that is always read whole).</summary>
    public static PropertyBuilder<CancellationPolicy> MapCancellationPolicy<TEntity>(
        this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, CancellationPolicy>> property)
        where TEntity : class =>
        builder.Property(property)
            .HasColumnType("jsonb")
            .HasConversion(
                policy => JsonSerializer.Serialize(policy.Tiers, JsonSerializerOptions.Default),
                json => CancellationPolicy.Create(
                    JsonSerializer.Deserialize<List<RefundTier>>(json, JsonSerializerOptions.Default)!).Value,
                new ValueComparer<CancellationPolicy>(
                    (a, b) => a == b,
                    policy => policy.GetHashCode(),
                    policy => policy));
}
