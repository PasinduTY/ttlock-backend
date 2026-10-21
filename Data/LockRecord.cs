namespace TTLockBackend.Data;

/// <summary>
/// One physical lock and the credential that controls it. One row per lock.
/// </summary>
/// <remarks>
/// Not named <c>Lock</c>: .NET 9+ has a built-in <c>System.Threading.Lock</c>,
/// and ASP.NET imports <c>System.Threading</c> everywhere, so that name would
/// be ambiguous in every file that used it.
/// </remarks>
public class LockRecord
{
    /// <summary>
    /// Bluetooth MAC, e.g. <c>B0:94:A1:E1:31:8F</c>. The primary key: the lock's
    /// identity, matching the <c>lock/&lt;MAC&gt;</c> key in the app's storage.
    /// </summary>
    public required string Mac { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// The admin credential for this lock. Anyone holding it can open and
    /// factory reset the lock. Stored encrypted (from step C onwards).
    /// </summary>
    public required string LockData { get; set; }

    /// <summary>
    /// SDK protocol descriptor, a JSON string from the scan. Kept alongside
    /// <see cref="LockData"/> because it cannot be derived from the MAC.
    /// </summary>
    public required string LockVersion { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
