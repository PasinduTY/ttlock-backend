using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TTLockBackend.Data;

/// <summary>
/// EF Core's view of the database. Each <see cref="DbSet{T}"/> is a table.
/// </summary>
public class AppDbContext(
    DbContextOptions<AppDbContext> options,
    IDataProtectionProvider dataProtection) : DbContext(options)
{
    /// <summary>
    /// Names what this protector is for. A protector only decrypts data made
    /// by one with the same purpose, so a future second secret column cannot
    /// accidentally decrypt this one. Changing the string makes every stored
    /// lockData unreadable, so it is versioned rather than ever edited.
    /// </summary>
    private const string LockDataPurpose = "TTLockBackend.LockData.v1";

    private static readonly ValueConverter<DateTime, DateTime> UtcConverter = new(
        toDatabase => toDatabase,
        fromDatabase => DateTime.SpecifyKind(fromDatabase, DateTimeKind.Utc));

    public DbSet<LockRecord> Locks => Set<LockRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // EF builds this model once and reuses it for every context, so the
        // protector captured here is shared. That is fine: protectors are
        // stateless apart from the key ring, which is the same for all.
        var lockDataProtector = dataProtection.CreateProtector(LockDataPurpose);

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

            // lockData is encrypted on every write and decrypted on every read.
            // Done here rather than in the endpoints so no endpoint can forget:
            // the rest of the code only ever sees plain text.
            entity.Property(l => l.LockData).HasConversion(
                plain => lockDataProtector.Protect(plain),
                stored => lockDataProtector.Unprotect(stored));
        });
    }
}
