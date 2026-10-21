using Microsoft.EntityFrameworkCore;

namespace TTLockBackend.Data;

/// <summary>
/// EF Core's view of the database. Each <see cref="DbSet{T}"/> is a table.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<LockRecord> Locks => Set<LockRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LockRecord>(entity =>
        {
            entity.ToTable("Locks");

            // EF only finds a key by convention when it is called "Id" or
            // "LockRecordId". Ours is the MAC, so it has to be stated.
            entity.HasKey(l => l.Mac);

            // "AA:BB:CC:DD:EE:FF" is always 17 characters.
            entity.Property(l => l.Mac).HasMaxLength(17);
            entity.Property(l => l.Name).HasMaxLength(100);
        });
    }
}
