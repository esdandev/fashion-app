using FashionApp.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FashionApp.Api.Infrastructure;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // The spec defines the persisted values in lowercase; the enum names are
    // PascalCase, so a plain ToString() conversion would drift from the API.
    private static readonly ValueConverter<OutfitSource, string> OutfitSourceConverter = new(
        source => source == OutfitSource.Ai ? "ai" : "manual",
        value => value == "ai" ? OutfitSource.Ai : OutfitSource.Manual);

    public DbSet<Garment> Garments => Set<Garment>();
    public DbSet<Outfit> Outfits => Set<Outfit>();
    public DbSet<OutfitItem> OutfitItems => Set<OutfitItem>();
    public DbSet<Digest> Digests => Set<Digest>();
    public DbSet<DigestItem> DigestItems => Set<DigestItem>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(SqlitePragmaInterceptor.Instance);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Every DateTimeOffset is stored as UTC ticks. This keeps timestamps sortable
        // in SQLite (which cannot ORDER BY DateTimeOffset) and normalizes any write
        // made with a non-UTC offset.
        configurationBuilder
            .Properties<DateTimeOffset>()
            .HaveConversion<UtcTicksDateTimeOffsetConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var garment = modelBuilder.Entity<Garment>();
        garment.ToTable("Garments");
        garment.HasKey(g => g.Id);
        garment.Property(g => g.Name).HasMaxLength(120).IsRequired();
        garment.Property(g => g.Category).HasConversion<string>().HasMaxLength(20).IsRequired();
        garment.Property(g => g.Subcategory).HasMaxLength(60);
        garment.Property(g => g.ColorName).HasMaxLength(40);
        garment.Property(g => g.ColorHex).HasMaxLength(7);
        garment.Property(g => g.SecondaryColorsJson).HasMaxLength(200);
        garment.Property(g => g.Pattern).HasMaxLength(40);
        garment.Property(g => g.Seasons).HasMaxLength(40);
        garment.Property(g => g.Material).HasMaxLength(60);
        garment.Property(g => g.Brand).HasMaxLength(60);
        garment.Property(g => g.StyleTagsJson).HasMaxLength(300);
        garment.Property(g => g.Notes).HasMaxLength(400);
        garment.Property(g => g.ImagePath).HasMaxLength(200).IsRequired();
        garment.Property(g => g.ThumbPath).HasMaxLength(200).IsRequired();
        garment.Property(g => g.ContentHash).HasMaxLength(64).IsRequired();
        garment.HasIndex(g => g.ContentHash).IsUnique();
        garment.HasIndex(g => g.IsArchived);
        garment.HasIndex(g => g.Category);
        garment.HasIndex(g => g.CreatedAt);
        // Covers the full CreatedAt DESC, Id DESC ordering used by list pagination.
        garment.HasIndex(g => new { g.CreatedAt, g.Id });

        var outfit = modelBuilder.Entity<Outfit>();
        outfit.ToTable("Outfits");
        outfit.HasKey(o => o.Id);
        outfit.Property(o => o.Name).HasMaxLength(120).IsRequired();
        outfit.Property(o => o.Occasion).HasMaxLength(60);
        outfit.Property(o => o.Source).HasConversion(OutfitSourceConverter).HasMaxLength(10).IsRequired();
        outfit.Property(o => o.Rationale).HasMaxLength(600);
        outfit.HasIndex(o => o.CreatedAt);
        outfit.HasIndex(o => o.IsFavorite);

        var outfitItem = modelBuilder.Entity<OutfitItem>();
        outfitItem.ToTable("OutfitItems");
        outfitItem.HasKey(i => new { i.OutfitId, i.GarmentId });
        outfitItem.HasOne(i => i.Outfit)
            .WithMany(o => o.Items)
            .HasForeignKey(i => i.OutfitId)
            .OnDelete(DeleteBehavior.Cascade);
        // Removing a garment from the catalog must never silently delete history:
        // an outfit keeps its pieces until the outfit itself is archived/deleted.
        outfitItem.HasOne(i => i.Garment)
            .WithMany()
            .HasForeignKey(i => i.GarmentId)
            .OnDelete(DeleteBehavior.Restrict);
        outfitItem.HasIndex(i => new { i.OutfitId, i.SortOrder });

        // A single digest per day: re-generating a date replaces the previous row.
        var digest = modelBuilder.Entity<Digest>();
        digest.ToTable("Digests");
        digest.HasKey(d => d.Id);
        digest.Property(d => d.Summary).HasMaxLength(600).IsRequired();
        digest.HasIndex(d => d.Date).IsUnique();

        var digestItem = modelBuilder.Entity<DigestItem>();
        digestItem.ToTable("DigestItems");
        digestItem.HasKey(i => i.Id);
        digestItem.Property(i => i.Title).HasMaxLength(200).IsRequired();
        digestItem.Property(i => i.Url).HasMaxLength(500).IsRequired();
        digestItem.Property(i => i.Source).HasMaxLength(80).IsRequired();
        digestItem.Property(i => i.Summary).HasMaxLength(600).IsRequired();
        digestItem.Property(i => i.StyleTagsJson).HasMaxLength(300).IsRequired();
        digestItem.Property(i => i.ImageUrl).HasMaxLength(500);
        digestItem.HasOne(i => i.Digest)
            .WithMany(d => d.Items)
            .HasForeignKey(i => i.DigestId)
            .OnDelete(DeleteBehavior.Cascade);
        // The same note may legitimately appear on consecutive days, so the URL
        // is unique only within its digest.
        digestItem.HasIndex(i => new { i.DigestId, i.Url }).IsUnique();
        digestItem.HasIndex(i => new { i.DigestId, i.SortOrder });
    }
}
