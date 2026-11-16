using System.Text;
using LicenseHub.Application.Admins;
using LicenseHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LicenseHub.Api.Commands;

/// <summary>
/// <c>admin create --email &lt;email&gt; [--password-stdin]</c>: creates an admin account in any environment.
/// The password is typed twice without echo, or read from standard input with --password-stdin.
/// </summary>
internal static class AdminCommand
{
    public const string Name = "admin";

    private const string Usage = "Usage: admin create --email <email> [--password-stdin]";

    public static async Task<int> RunAsync(WebApplication app, string[] args, TextReader input, TextWriter output, TextWriter error)
    {
        if (args is not [Name, "create", .. var options] || OptionValue(options, "--email") is not { } email)
        {
            await error.WriteLineAsync(Usage);
            return 2;
        }

        var password = options.Contains("--password-stdin", StringComparer.Ordinal)
            ? await input.ReadLineAsync()
            : ReadPasswordTwice(output, error);
        if (password is null)
        {
            return 1;
        }

        await using var scope = app.Services.CreateAsyncScope();
        if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
        {
            await scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>().Database.MigrateAsync();
        }

        var result = await scope.ServiceProvider.GetRequiredService<CreateAdminHandler>()
            .HandleAsync(new CreateAdminCommand(email, password), CancellationToken.None);
        if (!result.IsSuccess)
        {
            await error.WriteLineAsync($"Could not create the admin: {result.Error!.Message}");
            return 1;
        }

        await output.WriteLineAsync($"Admin created: {result.Value.Email} ({result.Value.Id})");
        return 0;
    }

    private static string? OptionValue(string[] options, string name)
    {
        var index = Array.IndexOf(options, name);
        return index >= 0 && index + 1 < options.Length ? options[index + 1] : null;
    }

    private static string? ReadPasswordTwice(TextWriter output, TextWriter error)
    {
        if (Console.IsInputRedirected)
        {
            error.WriteLine("No terminal to read the password from. Pipe it with --password-stdin instead.");
            return null;
        }

        output.Write($"Password (min. {CreateAdminHandler.MinPasswordLength} characters): ");
        var password = ReadHidden();
        output.Write("Repeat the password: ");
        var confirmation = ReadHidden();

        if (password != confirmation)
        {
            error.WriteLine("The passwords do not match.");
            return null;
        }

        return password;
    }

    /// <summary>Reads a line from the console without echoing it.</summary>
    private static string ReadHidden()
    {
        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }
    }
}
