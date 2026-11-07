using FashionApp.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FashionApp.Api.Infrastructure;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Garment> Garments => Set<Garment>();
    public DbSet<Outfit> Outfits => Set<Outfit>();

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

        var outfit = modelBuilder.Entity<Outfit>();
        outfit.ToTable("Outfits");
        outfit.HasKey(o => o.Id);
        outfit.Property(o => o.Name).HasMaxLength(120).IsRequired();
        outfit.Property(o => o.Occasion).HasMaxLength(60);
        outfit.Property(o => o.Source).HasConversion<string>().HasMaxLength(10).IsRequired();
        outfit.Property(o => o.Rationale).HasMaxLength(600);
        outfit.HasIndex(o => o.CreatedAt);
        outfit.HasIndex(o => o.IsFavorite);
    }
}
