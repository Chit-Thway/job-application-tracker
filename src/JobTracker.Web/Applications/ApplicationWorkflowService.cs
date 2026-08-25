using JobTracker.Web.Data;
using JobTracker.Web.Identity;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Web.Applications;

public sealed record WorkflowStatusItem(
    Guid Id,
    PipelineStage? PreviousStage,
    PipelineStage? NewStage,
    ApplicationOutcome? PreviousOutcome,
    ApplicationOutcome? NewOutcome,
    DateTimeOffset EffectiveAt,
    string? Note);

public sealed record WorkflowContactItem(
    Guid Id,
    string Name,
    string? JobTitle,
    string? Email,
    string? Phone,
    string? Notes);

public sealed record WorkflowInteractionItem(
    Guid Id,
    Guid? ContactId,
    string? ContactName,
    InteractionType Type,
    DateTimeOffset OccurredAt,
    bool IsEmployerResponse,
    string? Notes);

public sealed record WorkflowTaskItem(
    Guid Id,
    string Title,
    DateTimeOffset? DueAt,
    DateTimeOffset? CompletedAt,
    string? Notes);

public sealed record WorkflowAppointmentItem(
    Guid Id,
    AppointmentType Type,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string TimeZoneId,
    string? LocationOrLink,
    string? Notes);

public sealed record ApplicationWorkflowDetails(
    ApplicationDetails Application,
    string TimeZoneId,
    DateTimeOffset CurrentTime,
    int RetentionMonths,
    int DeletionGraceDays,
    bool CanConfirmGhosted,
    DateTimeOffset? FirstEmployerResponseAt,
    IReadOnlyList<WorkflowStatusItem> StatusHistory,
    IReadOnlyList<WorkflowContactItem> Contacts,
    IReadOnlyList<WorkflowInteractionItem> Interactions,
    IReadOnlyList<WorkflowTaskItem> Tasks,
    IReadOnlyList<WorkflowAppointmentItem> Appointments);

public sealed record StatusTransitionInput(
    PipelineStage Stage,
    ApplicationOutcome Outcome,
    string? Note);

public sealed record ContactInput(
    string Name,
    string? JobTitle,
    string? Email,
    string? Phone,
    string? Notes);

public sealed record InteractionInput(
    Guid? ContactId,
    InteractionType Type,
    DateTime OccurredAtLocal,
    bool IsEmployerResponse,
    string? Notes);

public sealed record WorkflowTaskInput(
    string Title,
    DateTime? DueAtLocal,
    string? Notes);

public sealed record WorkflowAppointmentInput(
    AppointmentType Type,
    DateTime StartsAtLocal,
    DateTime EndsAtLocal,
    string? LocationOrLink,
    string? Notes);

public enum WorkflowWriteResult
{
    Success,
    NotFound,
    InvalidRelationship,
    InvalidTransition,
    InvalidLocalTime,
    ContactInUse,
}

public sealed class ApplicationWorkflowService(
    ApplicationDbContext database,
    ICurrentUserContext currentUser,
    TimeProvider timeProvider)
{
    public async Task<ApplicationWorkflowDetails?> FindAsync(
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var application = await database.JobApplications
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == applicationId && item.OwnerId == ownerId,
                cancellationToken);
        if (application is null)
        {
            return null;
        }

        var companyName = application.CompanyId is null
            ? null
            : await database.Companies
                .AsNoTracking()
                .Where(company =>
                    company.Id == application.CompanyId
                    && company.OwnerId == ownerId)
                .Select(company => company.Name)
                .SingleOrDefaultAsync(cancellationToken);
        var preferences = await database.Users
            .AsNoTracking()
            .Where(user => user.Id == ownerId)
            .Select(user => new RetentionPreferences(
                user.TimeZoneId,
                user.RetentionMonths,
                user.DeletionGraceDays))
            .SingleAsync(cancellationToken);
        var timeZoneId = preferences.TimeZoneId;
        var currentTime = timeProvider.GetUtcNow();
        var localToday = DateOnly.FromDateTime(ApplicationTime.ToLocal(currentTime, timeZoneId));

        var history = await database.StatusHistory
            .AsNoTracking()
            .Where(item =>
                item.JobApplicationId == applicationId
                && item.OwnerId == ownerId)
            .OrderByDescending(item => item.EffectiveAt)
            .Select(item => new WorkflowStatusItem(
                item.Id,
                item.PreviousStage,
                item.NewStage,
                item.PreviousOutcome,
                item.NewOutcome,
                item.EffectiveAt,
                item.Note))
            .ToListAsync(cancellationToken);

        var contacts = await database.Contacts
            .AsNoTracking()
            .Where(item =>
                item.JobApplicationId == applicationId
                && item.OwnerId == ownerId)
            .OrderBy(item => item.Name)
            .Select(item => new WorkflowContactItem(
                item.Id,
                item.Name,
                item.JobTitle,
                item.Email,
                item.Phone,
                item.Notes))
            .ToListAsync(cancellationToken);

        var interactions = await database.Interactions
            .AsNoTracking()
            .Where(item =>
                item.JobApplicationId == applicationId
                && item.OwnerId == ownerId)
            .OrderByDescending(item => item.OccurredAt)
            .Select(item => new WorkflowInteractionItem(
                item.Id,
                item.ContactId,
                database.Contacts
                    .Where(contact =>
                        contact.Id == item.ContactId
                        && contact.OwnerId == ownerId)
                    .Select(contact => contact.Name)
                    .SingleOrDefault(),
                item.Type,
                item.OccurredAt,
                item.IsEmployerResponse,
                item.Notes))
            .ToListAsync(cancellationToken);

        var tasks = await database.Tasks
            .AsNoTracking()
            .Where(item =>
                item.JobApplicationId == applicationId
                && item.OwnerId == ownerId)
            .OrderBy(item => item.CompletedAt != null)
            .ThenBy(item => item.DueAt == null)
            .ThenBy(item => item.DueAt)
            .ThenBy(item => item.Title)
            .Select(item => new WorkflowTaskItem(
                item.Id,
                item.Title,
                item.DueAt,
                item.CompletedAt,
                item.Notes))
            .ToListAsync(cancellationToken);

        var appointments = await database.Appointments
            .AsNoTracking()
            .Where(item =>
                item.JobApplicationId == applicationId
                && item.OwnerId == ownerId)
            .OrderBy(item => item.StartsAt)
            .Select(item => new WorkflowAppointmentItem(
                item.Id,
                item.Type,
                item.StartsAt,
                item.EndsAt,
                item.TimeZoneId,
                item.LocationOrLink,
                item.Notes))
            .ToListAsync(cancellationToken);

        var appliedStart = ApplicationTime.TryConvertToUtc(
            application.AppliedOn.ToDateTime(TimeOnly.MinValue),
            timeZoneId,
            out var appliedStartUtc)
            ? appliedStartUtc
            : DateTimeOffset.MinValue;
        var firstResponse = interactions
            .Where(item => item.IsEmployerResponse && item.OccurredAt >= appliedStart)
            .Select(item => (DateTimeOffset?)item.OccurredAt)
            .Min();

        return new ApplicationWorkflowDetails(
            new ApplicationDetails(
                application.Id,
                application.CompanyId,
                companyName,
                application.RoleTitle,
                application.AppliedOn,
                application.Stage,
                application.Outcome,
                application.SourceUrl,
                application.ApplicationPortalUrl,
                application.SourceText,
                application.DescriptionText,
                application.ExtractionMetadataJson,
                application.Notes,
                application.IsSavedForever,
                application.DeletionScheduledAt),
            timeZoneId,
            currentTime,
            preferences.RetentionMonths,
            preferences.DeletionGraceDays,
            localToday >= application.AppliedOn.AddDays(30)
                && firstResponse is null
                && application.Outcome != ApplicationOutcome.Ghosted,
            firstResponse,
            history,
            contacts,
            interactions,
            tasks,
            appointments);
    }

    public async Task<WorkflowWriteResult> TransitionAsync(
        Guid applicationId,
        StatusTransitionInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ApplicationDisplay.IsActive(input.Stage) || !ApplicationDisplay.IsActive(input.Outcome))
        {
            return WorkflowWriteResult.InvalidTransition;
        }

        var ownerId = RequireOwnerId();
        var application = await database.JobApplications.SingleOrDefaultAsync(
            item => item.Id == applicationId && item.OwnerId == ownerId,
            cancellationToken);
        if (application is null)
        {
            return WorkflowWriteResult.NotFound;
        }

        var stageChanged = application.Stage != input.Stage;
        var outcomeChanged = application.Outcome != input.Outcome;
        if (!stageChanged && !outcomeChanged)
        {
            return WorkflowWriteResult.InvalidTransition;
        }

        if (outcomeChanged && input.Outcome == ApplicationOutcome.Ghosted)
        {
            var timeZoneId = await GetTimeZoneIdAsync(ownerId, cancellationToken);
            var localToday = DateOnly.FromDateTime(
                ApplicationTime.ToLocal(timeProvider.GetUtcNow(), timeZoneId));
            if (localToday < application.AppliedOn.AddDays(30))
            {
                return WorkflowWriteResult.InvalidTransition;
            }

            var appliedStart = ApplicationTime.TryConvertToUtc(
                application.AppliedOn.ToDateTime(TimeOnly.MinValue),
                timeZoneId,
                out var appliedStartUtc)
                ? appliedStartUtc
                : DateTimeOffset.MinValue;
            var hasMeaningfulResponse = await database.Interactions
                .AsNoTracking()
                .AnyAsync(item =>
                    item.OwnerId == ownerId
                    && item.JobApplicationId == application.Id
                    && item.IsEmployerResponse
                    && item.OccurredAt >= appliedStart,
                    cancellationToken);
            if (hasMeaningfulResponse)
            {
                return WorkflowWriteResult.InvalidTransition;
            }
        }

        var history = new StatusHistory
        {
            OwnerId = ownerId,
            JobApplicationId = application.Id,
            PreviousStage = stageChanged ? application.Stage : null,
            NewStage = stageChanged ? input.Stage : null,
            PreviousOutcome = outcomeChanged ? application.Outcome : null,
            NewOutcome = outcomeChanged ? input.Outcome : null,
            EffectiveAt = timeProvider.GetUtcNow(),
            Note = NullIfWhiteSpace(input.Note),
        };

        application.Stage = input.Stage;
        application.Outcome = input.Outcome;
        database.StatusHistory.Add(history);
        await database.SaveChangesAsync(cancellationToken);
        return WorkflowWriteResult.Success;
    }

    public async Task<(WorkflowWriteResult Result, Guid? Id)> AddContactAsync(
        Guid applicationId,
        ContactInput input,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var application = await FindOwnedApplicationAsync(
            applicationId,
            ownerId,
            cancellationToken);
        if (application is null)
        {
            return (WorkflowWriteResult.NotFound, null);
        }

        var contact = new Contact
        {
            OwnerId = ownerId,
            CompanyId = application.CompanyId,
            JobApplicationId = application.Id,
            Name = input.Name.Trim(),
            JobTitle = NullIfWhiteSpace(input.JobTitle),
            Email = NullIfWhiteSpace(input.Email),
            Phone = NullIfWhiteSpace(input.Phone),
            Notes = NullIfWhiteSpace(input.Notes),
        };

        database.Contacts.Add(contact);
        await database.SaveChangesAsync(cancellationToken);
        return (WorkflowWriteResult.Success, contact.Id);
    }

    public async Task<WorkflowWriteResult> DeleteContactAsync(
        Guid applicationId,
        Guid contactId,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var contact = await database.Contacts.SingleOrDefaultAsync(
            item =>
                item.Id == contactId
                && item.JobApplicationId == applicationId
                && item.OwnerId == ownerId,
            cancellationToken);
        if (contact is null)
        {
            return WorkflowWriteResult.NotFound;
        }

        if (await database.Interactions.AnyAsync(
                item => item.ContactId == contactId && item.OwnerId == ownerId,
                cancellationToken))
        {
            return WorkflowWriteResult.ContactInUse;
        }

        database.Contacts.Remove(contact);
        await database.SaveChangesAsync(cancellationToken);
        return WorkflowWriteResult.Success;
    }

    public async Task<(WorkflowWriteResult Result, Guid? Id)> AddInteractionAsync(
        Guid applicationId,
        InteractionInput input,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        if (!await ApplicationExistsAsync(applicationId, ownerId, cancellationToken))
        {
            return (WorkflowWriteResult.NotFound, null);
        }

        if (!await IsValidContactAsync(
                applicationId,
                input.ContactId,
                ownerId,
                cancellationToken))
        {
            return (WorkflowWriteResult.InvalidRelationship, null);
        }

        var timeZoneId = await GetTimeZoneIdAsync(ownerId, cancellationToken);
        if (!ApplicationTime.TryConvertToUtc(input.OccurredAtLocal, timeZoneId, out var occurredAt))
        {
            return (WorkflowWriteResult.InvalidLocalTime, null);
        }

        var interaction = new Interaction
        {
            OwnerId = ownerId,
            JobApplicationId = applicationId,
            ContactId = input.ContactId,
            Type = input.Type,
            OccurredAt = occurredAt,
            IsEmployerResponse = input.IsEmployerResponse,
            Notes = NullIfWhiteSpace(input.Notes),
        };

        database.Interactions.Add(interaction);
        await database.SaveChangesAsync(cancellationToken);
        return (WorkflowWriteResult.Success, interaction.Id);
    }

    public async Task<WorkflowWriteResult> UpdateInteractionAsync(
        Guid applicationId,
        Guid interactionId,
        InteractionInput input,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var interaction = await database.Interactions.SingleOrDefaultAsync(
            item =>
                item.Id == interactionId
                && item.JobApplicationId == applicationId
                && item.OwnerId == ownerId,
            cancellationToken);
        if (interaction is null)
        {
            return WorkflowWriteResult.NotFound;
        }

        if (!await IsValidContactAsync(
                applicationId,
                input.ContactId,
                ownerId,
                cancellationToken))
        {
            return WorkflowWriteResult.InvalidRelationship;
        }

        var timeZoneId = await GetTimeZoneIdAsync(ownerId, cancellationToken);
        if (!ApplicationTime.TryConvertToUtc(input.OccurredAtLocal, timeZoneId, out var occurredAt))
        {
            return WorkflowWriteResult.InvalidLocalTime;
        }

        interaction.ContactId = input.ContactId;
        interaction.Type = input.Type;
        interaction.OccurredAt = occurredAt;
        interaction.IsEmployerResponse = input.IsEmployerResponse;
        interaction.Notes = NullIfWhiteSpace(input.Notes);
        await database.SaveChangesAsync(cancellationToken);
        return WorkflowWriteResult.Success;
    }

    public async Task<WorkflowWriteResult> DeleteInteractionAsync(
        Guid applicationId,
        Guid interactionId,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var interaction = await database.Interactions.SingleOrDefaultAsync(
            item =>
                item.Id == interactionId
                && item.JobApplicationId == applicationId
                && item.OwnerId == ownerId,
            cancellationToken);
        if (interaction is null)
        {
            return WorkflowWriteResult.NotFound;
        }

        database.Interactions.Remove(interaction);
        await database.SaveChangesAsync(cancellationToken);
        return WorkflowWriteResult.Success;
    }

    public async Task<(WorkflowWriteResult Result, Guid? Id)> AddTaskAsync(
        Guid applicationId,
        WorkflowTaskInput input,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        if (!await ApplicationExistsAsync(applicationId, ownerId, cancellationToken))
        {
            return (WorkflowWriteResult.NotFound, null);
        }

        DateTimeOffset? dueAt = null;
        if (input.DueAtLocal is not null)
        {
            var timeZoneId = await GetTimeZoneIdAsync(ownerId, cancellationToken);
            if (!ApplicationTime.TryConvertToUtc(input.DueAtLocal.Value, timeZoneId, out var converted))
            {
                return (WorkflowWriteResult.InvalidLocalTime, null);
            }

            dueAt = converted;
        }

        var task = new TaskItem
        {
            OwnerId = ownerId,
            JobApplicationId = applicationId,
            Title = input.Title.Trim(),
            DueAt = dueAt,
            Notes = NullIfWhiteSpace(input.Notes),
        };

        database.Tasks.Add(task);
        await database.SaveChangesAsync(cancellationToken);
        return (WorkflowWriteResult.Success, task.Id);
    }

    public async Task<WorkflowWriteResult> SetTaskCompletedAsync(
        Guid applicationId,
        Guid taskId,
        bool completed,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var task = await database.Tasks.SingleOrDefaultAsync(
            item =>
                item.Id == taskId
                && item.JobApplicationId == applicationId
                && item.OwnerId == ownerId,
            cancellationToken);
        if (task is null)
        {
            return WorkflowWriteResult.NotFound;
        }

        task.CompletedAt = completed ? timeProvider.GetUtcNow() : null;
        await database.SaveChangesAsync(cancellationToken);
        return WorkflowWriteResult.Success;
    }

    public async Task<WorkflowWriteResult> DeleteTaskAsync(
        Guid applicationId,
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var task = await database.Tasks.SingleOrDefaultAsync(
            item =>
                item.Id == taskId
                && item.JobApplicationId == applicationId
                && item.OwnerId == ownerId,
            cancellationToken);
        if (task is null)
        {
            return WorkflowWriteResult.NotFound;
        }

        database.Tasks.Remove(task);
        await database.SaveChangesAsync(cancellationToken);
        return WorkflowWriteResult.Success;
    }

    public async Task<(WorkflowWriteResult Result, Guid? Id)> AddAppointmentAsync(
        Guid applicationId,
        WorkflowAppointmentInput input,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        if (!await ApplicationExistsAsync(applicationId, ownerId, cancellationToken))
        {
            return (WorkflowWriteResult.NotFound, null);
        }

        var timeZoneId = await GetTimeZoneIdAsync(ownerId, cancellationToken);
        if (!ApplicationTime.TryConvertToUtc(input.StartsAtLocal, timeZoneId, out var startsAt)
            || !ApplicationTime.TryConvertToUtc(input.EndsAtLocal, timeZoneId, out var endsAt)
            || endsAt <= startsAt)
        {
            return (WorkflowWriteResult.InvalidLocalTime, null);
        }

        var appointment = new Appointment
        {
            OwnerId = ownerId,
            JobApplicationId = applicationId,
            Type = input.Type,
            StartsAt = startsAt,
            EndsAt = endsAt,
            TimeZoneId = timeZoneId,
            LocationOrLink = NullIfWhiteSpace(input.LocationOrLink),
            Notes = NullIfWhiteSpace(input.Notes),
        };

        database.Appointments.Add(appointment);
        await database.SaveChangesAsync(cancellationToken);
        return (WorkflowWriteResult.Success, appointment.Id);
    }

    public async Task<WorkflowWriteResult> DeleteAppointmentAsync(
        Guid applicationId,
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var appointment = await database.Appointments.SingleOrDefaultAsync(
            item =>
                item.Id == appointmentId
                && item.JobApplicationId == applicationId
                && item.OwnerId == ownerId,
            cancellationToken);
        if (appointment is null)
        {
            return WorkflowWriteResult.NotFound;
        }

        database.Appointments.Remove(appointment);
        await database.SaveChangesAsync(cancellationToken);
        return WorkflowWriteResult.Success;
    }

    private async Task<JobApplication?> FindOwnedApplicationAsync(
        Guid applicationId,
        string ownerId,
        CancellationToken cancellationToken) =>
        await database.JobApplications.SingleOrDefaultAsync(
            item => item.Id == applicationId && item.OwnerId == ownerId,
            cancellationToken);

    private async Task<bool> ApplicationExistsAsync(
        Guid applicationId,
        string ownerId,
        CancellationToken cancellationToken) =>
        await database.JobApplications.AnyAsync(
            item => item.Id == applicationId && item.OwnerId == ownerId,
            cancellationToken);

    private async Task<bool> IsValidContactAsync(
        Guid applicationId,
        Guid? contactId,
        string ownerId,
        CancellationToken cancellationToken) =>
        contactId is null || await database.Contacts.AnyAsync(
            item =>
                item.Id == contactId
                && item.JobApplicationId == applicationId
                && item.OwnerId == ownerId,
            cancellationToken);

    private async Task<string> GetTimeZoneIdAsync(
        string ownerId,
        CancellationToken cancellationToken)
    {
        var timeZoneId = await database.Users
            .AsNoTracking()
            .Where(user => user.Id == ownerId)
            .Select(user => user.TimeZoneId)
            .SingleAsync(cancellationToken);
        return ApplicationTime.IsSupported(timeZoneId) ? timeZoneId : TimeZoneInfo.Utc.Id;
    }

    private string RequireOwnerId() =>
        currentUser.UserId
        ?? throw new InvalidOperationException("An authenticated user is required.");

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class ApplicationTime
{
    public static bool TryConvertToUtc(
        DateTime localDateTime,
        string timeZoneId,
        out DateTimeOffset utcValue)
    {
        utcValue = default;
        if (!TryFind(timeZoneId, out var timeZone))
        {
            return false;
        }

        var unspecified = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(unspecified))
        {
            return false;
        }

        var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(unspecified, timeZone);
        utcValue = new DateTimeOffset(utcDateTime, TimeSpan.Zero);
        return true;
    }

    public static DateTime ToLocal(DateTimeOffset value, string timeZoneId)
    {
        var timeZone = TryFind(timeZoneId, out var found) ? found : TimeZoneInfo.Utc;
        return DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTime(value, timeZone).DateTime,
            DateTimeKind.Unspecified);
    }

    public static string Display(DateTimeOffset value, string timeZoneId) =>
        $"{ToLocal(value, timeZoneId):d MMM yyyy, h:mm tt} ({timeZoneId})";

    public static bool IsSupported(string timeZoneId) => TryFind(timeZoneId, out _);

    private static bool TryFind(string timeZoneId, out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            timeZone = TimeZoneInfo.Utc;
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            timeZone = TimeZoneInfo.Utc;
            return false;
        }
    }
}
