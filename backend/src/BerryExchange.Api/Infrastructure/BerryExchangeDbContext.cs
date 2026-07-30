using BerryExchange.Api.Accounts;
using BerryExchange.Api.Chat;
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
    public DbSet<ListingPhoto> ListingPhotos => Set<ListingPhoto>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasPostgresExtension("vector");

        builder.Entity<Listing>(entity =>
        {
            entity.Property(l => l.BerryType).HasMaxLength(40).IsRequired();
            entity.Property(l => l.FarmName).HasMaxLength(40).IsRequired();
            entity.Property(l => l.Note).HasMaxLength(80);
            entity.Property(l => l.PricePerKg).HasColumnType("numeric(10,2)");
            entity.Property(l => l.QuantityAvailableKg).HasColumnType("numeric(10,2)");
            entity.Property(l => l.AiTastingNotes).HasMaxLength(300);
            entity.Property(l => l.PhotoContentType).HasMaxLength(40);
            entity.Property(l => l.Embedding).HasColumnType("vector(384)");
            entity.HasIndex(l => l.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops");
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(l => l.SellerId);
        });

        builder.Entity<ListingPhoto>(entity =>
        {
            entity.HasKey(p => p.ListingId);
            entity.HasOne<Listing>().WithOne().HasForeignKey<ListingPhoto>(p => p.ListingId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Reservation>(entity =>
        {
            entity.Property(r => r.QuantityKg).HasColumnType("numeric(10,2)");
            entity.HasOne<Listing>().WithMany().HasForeignKey(r => r.ListingId);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.BuyerId);
        });

        builder.Entity<ChatConversation>(entity =>
        {
            entity.Property(c => c.Title).HasMaxLength(80).IsRequired();
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(c => c.UserId);
        });
        builder.Entity<ChatMessage>(entity =>
        {
            entity.Property(m => m.Role).HasMaxLength(16).IsRequired();
            entity.Property(m => m.Content).HasMaxLength(4000).IsRequired();
            entity.HasOne<ChatConversation>().WithMany().HasForeignKey(m => m.ConversationId);
            entity.HasIndex(m => new { m.ConversationId, m.CreatedAt });
        });
    }
}
