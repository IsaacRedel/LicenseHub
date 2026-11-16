using LicenseHub.Domain.Common;
using LicenseHub.Domain.Licenses;
using LicenseHub.Domain.Payments;
using LicenseHub.Domain.Products;
using LicenseHub.Domain.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LicenseHub.Infrastructure.Persistence;

/// <summary>
/// Opaque identifiers sent by clients or gateways compare byte for byte, like C# ordinal equality.
/// The default utf8mb4 collation is case- and accent-insensitive, which would merge distinct values.
/// </summary>
internal static class BinaryCollation
{
    public const string Name = "utf8mb4_bin";
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Name).HasMaxLength(Limits.ProductNameLength).IsRequired();
        builder.Property(p => p.Slug).HasMaxLength(Limits.ProductSlugLength).IsRequired();
        builder.HasIndex(p => p.Slug).IsUnique();
    }
}

internal sealed class LicenseConfiguration : IEntityTypeConfiguration<License>
{
    public void Configure(EntityTypeBuilder<License> builder)
    {
        builder.ToTable("licenses");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.CustomerEmail).HasMaxLength(Limits.EmailLength).IsRequired();
        builder.Property(l => l.KeyHash)
            .HasColumnType($"binary({LicenseKey.HashLength})")
            .IsRequired();
        builder.Property(l => l.Status).HasConversion<string>().HasMaxLength(16);

        builder.HasIndex(l => l.KeyHash).IsUnique();
        builder.HasIndex(l => l.Status);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ActivationConfiguration : IEntityTypeConfiguration<Activation>
{
    public void Configure(EntityTypeBuilder<Activation> builder)
    {
        builder.ToTable("activations");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.ServerIdentifier)
            .HasMaxLength(Limits.ServerIdentifierLength)
            .UseCollation(BinaryCollation.Name)
            .IsRequired();

        builder.HasIndex(a => new { a.LicenseId, a.ServerIdentifier }).IsUnique();

        builder.HasOne<License>()
            .WithMany()
            .HasForeignKey(a => a.LicenseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ValidationLogConfiguration : IEntityTypeConfiguration<ValidationLog>
{
    public void Configure(EntityTypeBuilder<ValidationLog> builder)
    {
        builder.ToTable("validation_logs");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Property(v => v.Outcome).HasConversion<string>().HasMaxLength(32);
        builder.Property(v => v.IpAddress).HasMaxLength(Limits.IpAddressLength);

        builder.HasIndex(v => new { v.LicenseId, v.CreatedAt });
        builder.HasIndex(v => v.CreatedAt);

        builder.HasOne<License>()
            .WithMany()
            .HasForeignKey(v => v.LicenseId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class PaymentEventConfiguration : IEntityTypeConfiguration<PaymentEvent>
{
    public void Configure(EntityTypeBuilder<PaymentEvent> builder)
    {
        builder.ToTable("payment_events");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Gateway).HasMaxLength(Limits.GatewayLength).IsRequired();
        builder.Property(p => p.ExternalEventId)
            .HasMaxLength(Limits.ExternalEventIdLength)
            .UseCollation(BinaryCollation.Name)
            .IsRequired();

        builder.HasIndex(p => new { p.Gateway, p.ExternalEventId }).IsUnique();

        builder.HasOne<License>()
            .WithMany()
            .HasForeignKey(p => p.LicenseId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
