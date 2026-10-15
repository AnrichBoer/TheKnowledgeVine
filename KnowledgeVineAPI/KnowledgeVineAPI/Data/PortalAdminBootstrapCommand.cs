using System.Text;

namespace KnowledgeVineAPI.Data;

public static class PortalAdminBootstrapCommand
{
    public static PortalAdminDetails Parse(string[] args)
    {
        var details = PortalAdminDetails.Default;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--bootstrap-admin") continue;
            if (args[i] is not ("--email" or "--first-name" or "--last-name") || i + 1 == args.Length
                || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new PortalAdminBootstrapException("Supported options: --bootstrap-admin, --email, --first-name, --last-name. Passwords use protected input only.");
            var option = args[i++];
            details = option switch
            {
                "--email" => details with { Email = args[i] },
                "--first-name" => details with { FirstName = args[i] },
                _ => details with { LastName = args[i] }
            };
        }
        return details;
    }

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        try
        {
            var details = Parse(args);
            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<PortalAdminBootstrap>()
                .RunAsync(details, ReadPassword);
            Console.WriteLine(result == PortalAdminBootstrapResult.Created
                ? $"Initial admin created: {details.Email}. Active, email confirmed, Admin role assigned; stored password hash verified."
                : $"Admin already exists: {details.Email}. Existing credentials and account were preserved.");
            return 0;
        }
        catch (PortalAdminBootstrapException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Admin bootstrap cancelled.");
            return 130;
        }
        catch (Exception exception)
        {
            // Configuration/database exceptions can contain secrets; never print their raw message.
            Console.Error.WriteLine($"Admin bootstrap failed ({exception.GetType().Name}). Check the KnowledgeVine connection and schema.");
            return 1;
        }
    }

    private static string ReadPassword()
    {
        var password = ReadHidden("Initial admin password (hidden): ");
        var confirmation = ReadHidden("Confirm password (hidden): ");
        if (password != confirmation)
            throw new PortalAdminBootstrapException("Passwords do not match; no account was created.");
        return password;
    }

    private static string ReadHidden(string prompt)
    {
        Console.Write(prompt);
        if (Console.IsInputRedirected)
        {
            var value = Console.ReadLine() ?? throw new PortalAdminBootstrapException("Password input is missing.");
            Console.WriteLine();
            return value;
        }
        var valueBuilder = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                throw new OperationCanceledException();
            if (key.Key == ConsoleKey.Backspace)
            {
                if (valueBuilder.Length > 0) valueBuilder.Length--;
            }
            else if (!char.IsControl(key.KeyChar)) valueBuilder.Append(key.KeyChar);
        }
        Console.WriteLine();
        return valueBuilder.ToString();
    }
}
