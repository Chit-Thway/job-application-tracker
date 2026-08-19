namespace JobTracker.Web.Identity;

public sealed class InvitationCommandRunner(
    InvitationService invitations,
    IHostEnvironment environment,
    IConfiguration configuration)
{
    public async Task<int?> TryRunAsync(
        string[] arguments,
        CancellationToken cancellationToken = default)
    {
        if (arguments.Length == 0
            || !string.Equals(arguments[0], "invitations", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var confirmedProductionCommand = arguments.Any(argument =>
            string.Equals(argument, "--confirm-production", StringComparison.OrdinalIgnoreCase));
        var commandArguments = arguments
            .Where(argument => !string.Equals(
                argument,
                "--confirm-production",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (!environment.IsDevelopment()
            && (!configuration.GetValue<bool>("InvitationCommands:Enabled")
                || !confirmedProductionCommand))
        {
            Console.Error.WriteLine(
                "Invitation commands are disabled outside Development. " +
                "Temporarily set InvitationCommands:Enabled=true and add --confirm-production.");
            return 1;
        }

        if (commandArguments.Length < 2)
        {
            WriteUsage();
            return 1;
        }

        return commandArguments[1].ToLowerInvariant() switch
        {
            "create" => await CreateAsync(commandArguments, cancellationToken),
            "list" => await ListAsync(commandArguments, cancellationToken),
            "revoke" => await RevokeAsync(commandArguments, cancellationToken),
            _ => UnknownCommand(),
        };
    }

    private async Task<int> CreateAsync(
        string[] arguments,
        CancellationToken cancellationToken)
    {
        var validForDays = 7;
        if (arguments.Length == 4
            && string.Equals(arguments[2], "--days", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(arguments[3], out var parsedDays))
        {
            validForDays = parsedDays;
        }
        else if (arguments.Length != 2)
        {
            WriteUsage();
            return 1;
        }

        if (validForDays is < 1 or > 30)
        {
            Console.Error.WriteLine("--days must be a whole number from 1 to 30.");
            return 1;
        }

        var invitation = await invitations.CreateAsync(validForDays, cancellationToken);
        Console.WriteLine($"Invitation ID: {invitation.Id}");
        Console.WriteLine($"Expires (UTC): {invitation.ExpiresAt:O}");
        Console.WriteLine("Invitation code (shown once; keep it private):");
        Console.WriteLine(invitation.Code);
        return 0;
    }

    private async Task<int> ListAsync(
        string[] arguments,
        CancellationToken cancellationToken)
    {
        if (arguments.Length != 2)
        {
            WriteUsage();
            return 1;
        }

        var summaries = await invitations.ListAsync(cancellationToken);
        if (summaries.Count == 0)
        {
            Console.WriteLine("No invitations have been created.");
            return 0;
        }

        foreach (var invitation in summaries)
        {
            Console.WriteLine(
                $"{invitation.Id}  {invitation.Status,-9}  expires {invitation.ExpiresAt:O}");
        }

        return 0;
    }

    private async Task<int> RevokeAsync(
        string[] arguments,
        CancellationToken cancellationToken)
    {
        if (arguments.Length != 3 || !Guid.TryParse(arguments[2], out var id))
        {
            WriteUsage();
            return 1;
        }

        var revoked = await invitations.RevokeAsync(id, cancellationToken);
        if (!revoked)
        {
            Console.Error.WriteLine("Invitation was not found, was already used, or was already revoked.");
            return 1;
        }

        Console.WriteLine($"Revoked invitation {id}.");
        return 0;
    }

    private static int UnknownCommand()
    {
        WriteUsage();
        return 1;
    }

    private static void WriteUsage()
    {
        Console.WriteLine("Invitation commands:");
        Console.WriteLine("  invitations create [--days 1-30]");
        Console.WriteLine("  invitations list");
        Console.WriteLine("  invitations revoke <invitation-id>");
        Console.WriteLine(
            "  Outside Development, temporarily enable InvitationCommands:Enabled " +
            "and append --confirm-production.");
    }
}
