using BerryExchange.Api.Accounts;
using BerryExchange.Api.Listings;
using BerryExchange.Api.Reservations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BerryExchange.Api.Infrastructure;

public class BerryExchangeDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public BerryExchangeDbContext(DbContextOptions<BerryExchangeDbContext> options) : base(options) { }

    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasPostgresExtension("vector");

        builder.Entity<Listing>(entity =>
        {
            entity.Property(l => l.BerryType).HasMaxLength(40).IsRequired();
            entity.Property(l => l.FarmName).HasMaxLength(40).IsRequired();
            entity.Property(l => l.Note).HasMaxLength(80);
            entity.Property(l => l.PricePerPint).HasColumnType("numeric(10,2)");
            entity.Property(l => l.AiTastingNotes).HasMaxLength(300);
            entity.Property(l => l.Embedding).HasColumnType("vector(384)");
            entity.HasIndex(l => l.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops");
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(l => l.SellerId);
        });

        builder.Entity<Reservation>(entity =>
        {
            entity.HasOne<Listing>().WithMany().HasForeignKey(r => r.ListingId);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.BuyerId);
        });
    }
}
