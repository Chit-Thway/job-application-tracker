using System.Net.Mail;

namespace JobTracker.Web.Identity;

public sealed class InvitationCommandRunner(
    InvitationService invitations,
    IAccountEmailSender emailSender,
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
        string? recipientAddress = null;

        for (var index = 2; index < arguments.Length; index += 2)
        {
            if (index + 1 >= arguments.Length)
            {
                WriteUsage();
                return 1;
            }

            if (string.Equals(arguments[index], "--days", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(arguments[index + 1], out var parsedDays))
            {
                validForDays = parsedDays;
                continue;
            }

            if (string.Equals(arguments[index], "--email", StringComparison.OrdinalIgnoreCase)
                && MailAddress.TryCreate(arguments[index + 1].Trim(), out var parsedAddress))
            {
                recipientAddress = parsedAddress.Address;
                continue;
            }

            WriteUsage();
            return 1;
        }

        if (validForDays is < 1 or > 30)
        {
            Console.Error.WriteLine("--days must be a whole number from 1 to 30.");
            return 1;
        }

        var invitation = recipientAddress is null
            ? await invitations.CreateAsync(validForDays, cancellationToken)
            : await invitations.CreateForRecipientAsync(
                validForDays,
                recipientAddress,
                "command-line",
                cancellationToken);
        Console.WriteLine($"Invitation ID: {invitation.Id}");
        Console.WriteLine($"Expires (UTC): {invitation.ExpiresAt:O}");

        if (recipientAddress is not null)
        {
            try
            {
                await emailSender.SendInvitationAsync(
                    recipientAddress,
                    invitation.Code,
                    invitation.ExpiresAt);
            }
            catch
            {
                await invitations.RevokeAsync(invitation.Id, cancellationToken);
                Console.Error.WriteLine(
                    "The invitation email could not be sent. The unused invitation was revoked.");
                return 1;
            }

            Console.WriteLine("Invitation email accepted by the configured provider.");
            return 0;
        }

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
        Console.WriteLine("  invitations create [--days 1-30] [--email recipient@example.com]");
        Console.WriteLine("  invitations list");
        Console.WriteLine("  invitations revoke <invitation-id>");
        Console.WriteLine(
            "  Outside Development, temporarily enable InvitationCommands:Enabled " +
            "and append --confirm-production.");
    }
}
