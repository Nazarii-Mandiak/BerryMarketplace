using BerryExchange.Api.Accounts;
using BerryExchange.Api.Listings;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BerryExchange.Api.Infrastructure;

public class BerryExchangeDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public BerryExchangeDbContext(DbContextOptions<BerryExchangeDbContext> options) : base(options) { }

    public DbSet<Listing> Listings => Set<Listing>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Listing>(entity =>
        {
            entity.Property(l => l.BerryType).HasMaxLength(40).IsRequired();
            entity.Property(l => l.FarmName).HasMaxLength(40).IsRequired();
            entity.Property(l => l.Note).HasMaxLength(80);
            entity.Property(l => l.PricePerPint).HasColumnType("numeric(10,2)");
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(l => l.SellerId);
        });
    }
}
