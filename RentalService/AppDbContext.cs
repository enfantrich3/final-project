using Microsoft.EntityFrameworkCore;
using RentalService.Models;

namespace RentalService;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<Property> Properties { get; set; }
    public DbSet<PropertyPhoto> PropertyPhotos { get; set; }
    public DbSet<PriceRate> PriceRates { get; set; }
    public DbSet<Booking> Bookings { get; set; }
    public DbSet<Payment> Payments { get; set; }
    public DbSet<Review> Reviews { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<Property>()
            .HasOne(p => p.Owner)
            .WithMany()
            .HasForeignKey(p => p.OwnerId);

        modelBuilder.Entity<Booking>()
            .HasOne(b => b.Property)
            .WithMany(p => p.Bookings)
            .HasForeignKey(b => b.PropertyId);

        modelBuilder.Entity<Booking>()
            .HasOne(b => b.Renter)
            .WithMany()
            .HasForeignKey(b => b.RenterId);

        modelBuilder.Entity<Payment>()
            .HasOne(p => p.Booking)
            .WithOne(b => b.Payment)
            .HasForeignKey<Payment>(p => p.BookingId);

        modelBuilder.Entity<Review>()
            .HasOne(r => r.Property)
            .WithMany()
            .HasForeignKey(r => r.PropertyId);

        modelBuilder.Entity<Review>()
            .HasOne(r => r.Author)
            .WithMany()
            .HasForeignKey(r => r.AuthorId);
    }
}