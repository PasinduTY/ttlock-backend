using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TTLockBackend.Data;

/// <summary>
/// EF Core's view of the database. Each <see cref="DbSet{T}"/> is a table.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    private static readonly ValueConverter<DateTime, DateTime> UtcConverter = new(
        toDatabase => toDatabase,
        fromDatabase => DateTime.SpecifyKind(fromDatabase, DateTimeKind.Utc));

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

            // SQLite stores dates as plain text with no timezone, so they come
            // back as DateTimeKind.Unspecified and serialise without the "Z"
            // that marks UTC. A client would then read them as local time.
            // We only ever write UTC, so stamp that back on when reading.
            entity.Property(l => l.CreatedAt).HasConversion(UtcConverter);
            entity.Property(l => l.UpdatedAt).HasConversion(UtcConverter);
        });
    }
}
