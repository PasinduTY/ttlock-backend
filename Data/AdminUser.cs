namespace TTLockBackend.Data;

/// <summary>
/// Someone allowed to log in. Every account is an admin with full rights
/// over every lock; there is no lesser role.
/// </summary>
public class AdminUser
{
    public int Id { get; set; }

    /// <summary>
    /// Lower-case, so "Pasindu" and "pasindu" are the same account. Unique.
    /// </summary>
    public required string Username { get; set; }

    /// <summary>
    /// Never the password itself. A salted, deliberately slow hash produced by
    /// ASP.NET's <c>PasswordHasher</c>; it can check a password, not reveal one.
    /// </summary>
    public required string PasswordHash { get; set; }

    public DateTime CreatedAt { get; set; }
}
