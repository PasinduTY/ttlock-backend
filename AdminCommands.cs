using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TTLockBackend.Data;

namespace TTLockBackend;

/// <summary>
/// Admin account management, run on the server machine itself:
/// <code>
/// dotnet run -- add-admin &lt;username&gt;
/// dotnet run -- list-admins
/// dotnet run -- remove-admin &lt;username&gt;
/// </code>
/// There is deliberately no HTTP endpoint for any of this. A public "register"
/// endpoint would let anyone who can reach the server make themselves an admin.
/// </summary>
public static class AdminCommands
{
    public const int MinPasswordLength = 12;

    private static readonly Regex UsernamePattern =
        new("^[a-z0-9._-]{3,32}$", RegexOptions.Compiled);

    /// <summary>
    /// Runs the command named in <paramref name="args"/>, if any, and returns its
    /// exit code. Returns null when <paramref name="args"/> isn't a command, so
    /// the caller starts the server as normal.
    /// </summary>
    public static async Task<int?> TryRunAsync(WebApplication app, string[] args)
    {
        if (args is not ["add-admin" or "list-admins" or "remove-admin", ..])
        {
            return null;
        }

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return args switch
        {
            ["add-admin", var name] => await AddAsync(db, name),
            ["remove-admin", var name] => await RemoveAsync(db, name),
            ["list-admins"] => await ListAsync(db),
            _ => Fail("Usage: add-admin <username> | remove-admin <username> | list-admins"),
        };
    }

    private static async Task<int> AddAsync(AppDbContext db, string rawName)
    {
        var username = NormaliseUsername(rawName);
        if (username is null)
        {
            return Fail("Username must be 3-32 characters: letters, digits, '.', '_' or '-'.");
        }
        if (await db.Admins.AnyAsync(a => a.Username == username))
        {
            return Fail($"Admin '{username}' already exists.");
        }

        var password = ReadPassword($"Password for '{username}': ");
        if (password.Length < MinPasswordLength)
        {
            return Fail($"Password must be at least {MinPasswordLength} characters.");
        }
        if (ReadPassword("Repeat it: ") != password)
        {
            return Fail("The passwords did not match. Nothing was saved.");
        }

        var admin = new AdminUser
        {
            Username = username,
            PasswordHash = "",
            CreatedAt = DateTime.UtcNow,
        };
        admin.PasswordHash = new PasswordHasher<AdminUser>().HashPassword(admin, password);

        db.Admins.Add(admin);
        await db.SaveChangesAsync();

        Console.WriteLine($"Admin '{username}' added.");
        return 0;
    }

    private static async Task<int> RemoveAsync(AppDbContext db, string rawName)
    {
        var username = NormaliseUsername(rawName);
        var admin = username is null
            ? null
            : await db.Admins.SingleOrDefaultAsync(a => a.Username == username);
        if (admin is null)
        {
            return Fail($"No admin named '{rawName}'.");
        }

        db.Admins.Remove(admin);
        await db.SaveChangesAsync();

        Console.WriteLine($"Admin '{username}' removed.");
        Console.WriteLine(
            "Their access ends on their next request. Note: any lockData already on " +
            "their phone still opens locks; only resetting a lock revokes that.");
        return 0;
    }

    private static async Task<int> ListAsync(AppDbContext db)
    {
        var admins = await db.Admins.OrderBy(a => a.Username).ToListAsync();
        if (admins.Count == 0)
        {
            Console.WriteLine("No admins. Add one with: dotnet run -- add-admin <username>");
            return 0;
        }

        foreach (var admin in admins)
        {
            Console.WriteLine($"{admin.Username,-32} added {admin.CreatedAt:yyyy-MM-dd HH:mm} UTC");
        }
        return 0;
    }

    /// <summary>
    /// Lower-cases and checks the username. Returns null if it isn't valid.
    /// </summary>
    public static string? NormaliseUsername(string? raw)
    {
        var candidate = raw?.Trim().ToLowerInvariant();
        return candidate is not null && UsernamePattern.IsMatch(candidate) ? candidate : null;
    }

    /// <summary>
    /// Reads a password, showing a <c>*</c> per character instead of the
    /// character itself: enough feedback to see that keys are registering,
    /// without revealing the password (only its length). If input is piped in
    /// (scripts, tests) there is no screen to hide it from, so a plain line is read.
    /// </summary>
    private static string ReadPassword(string prompt)
    {
        Console.Write(prompt);

        if (Console.IsInputRedirected)
        {
            var line = Console.ReadLine() ?? "";
            Console.WriteLine();
            return line;
        }

        var typed = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (typed.Length > 0)
                {
                    typed.Length--;
                    Console.Write("\b \b"); // step back, blank the *, step back
                }
                continue;
            }
            if (!char.IsControl(key.KeyChar))
            {
                typed.Append(key.KeyChar);
                Console.Write('*');
            }
        }
        Console.WriteLine();
        return typed.ToString();
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
