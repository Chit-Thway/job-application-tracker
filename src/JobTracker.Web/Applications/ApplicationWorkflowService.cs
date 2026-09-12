using JobTracker.Web.Data;
using JobTracker.Web.Identity;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Web.Applications;

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
        var application = await FindApplicationAsync(applicationId, ownerId, cancellationToken);
        if (application is null)
        {
            return null;
        }

        var companyName = await FindCompanyNameAsync(application.CompanyId, ownerId, cancellationToken);
        var preferences = await GetRetentionPreferencesAsync(ownerId, cancellationToken);
        var timeZoneId = preferences.TimeZoneId;
        var currentTime = timeProvider.GetUtcNow();
        var localToday = DateOnly.FromDateTime(ApplicationTime.ToLocal(currentTime, timeZoneId));

        var history = await GetStatusHistoryAsync(applicationId, ownerId, cancellationToken);
        var contacts = await GetContactsAsync(applicationId, ownerId, cancellationToken);
        var interactions = await GetInteractionsAsync(applicationId, ownerId, cancellationToken);
        var tasks = await GetTasksAsync(applicationId, ownerId, cancellationToken);
        var appointments = await GetAppointmentsAsync(applicationId, ownerId, cancellationToken);

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
            ToApplicationDetails(application, companyName),
            timeZoneId,
            currentTime,
            preferences.RetentionMonths,
            preferences.DeletionGraceDays,
            CanConfirmGhosted(application, localToday, firstResponse),
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
            if (localToday < application.AppliedOn.AddDays(ApplicationRules.GhostingConfirmationAfterDays))
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

    private async Task<JobApplication?> FindApplicationAsync(
        Guid applicationId,
        string ownerId,
        CancellationToken cancellationToken) =>
        await database.JobApplications
            .AsNoTracking()
            .SingleOrDefaultAsync(
                application => application.Id == applicationId && application.OwnerId == ownerId,
                cancellationToken);

    private async Task<string?> FindCompanyNameAsync(
        Guid? companyId,
        string ownerId,
        CancellationToken cancellationToken)
    {
        if (companyId is null)
        {
            return null;
        }

        return await database.Companies
            .AsNoTracking()
            .Where(company => company.Id == companyId && company.OwnerId == ownerId)
            .Select(company => company.Name)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<RetentionPreferences> GetRetentionPreferencesAsync(
        string ownerId,
        CancellationToken cancellationToken) =>
        await database.Users
            .AsNoTracking()
            .Where(user => user.Id == ownerId)
            .Select(user => new RetentionPreferences(
                user.TimeZoneId,
                user.RetentionMonths,
                user.DeletionGraceDays))
            .SingleAsync(cancellationToken);

    private async Task<IReadOnlyList<WorkflowStatusItem>> GetStatusHistoryAsync(
        Guid applicationId,
        string ownerId,
        CancellationToken cancellationToken) =>
        await database.StatusHistory
            .AsNoTracking()
            .Where(history =>
                history.JobApplicationId == applicationId
                && history.OwnerId == ownerId)
            .OrderByDescending(history => history.EffectiveAt)
            .Select(history => new WorkflowStatusItem(
                history.Id,
                history.PreviousStage,
                history.NewStage,
                history.PreviousOutcome,
                history.NewOutcome,
                history.EffectiveAt,
                history.Note))
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<WorkflowContactItem>> GetContactsAsync(
        Guid applicationId,
        string ownerId,
        CancellationToken cancellationToken) =>
        await database.Contacts
            .AsNoTracking()
            .Where(contact =>
                contact.JobApplicationId == applicationId
                && contact.OwnerId == ownerId)
            .OrderBy(contact => contact.Name)
            .Select(contact => new WorkflowContactItem(
                contact.Id,
                contact.Name,
                contact.JobTitle,
                contact.Email,
                contact.Phone,
                contact.Notes))
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<WorkflowInteractionItem>> GetInteractionsAsync(
        Guid applicationId,
        string ownerId,
        CancellationToken cancellationToken) =>
        await database.Interactions
            .AsNoTracking()
            .Where(interaction =>
                interaction.JobApplicationId == applicationId
                && interaction.OwnerId == ownerId)
            .OrderByDescending(interaction => interaction.OccurredAt)
            .Select(interaction => new WorkflowInteractionItem(
                interaction.Id,
                interaction.ContactId,
                database.Contacts
                    .Where(contact =>
                        contact.Id == interaction.ContactId
                        && contact.OwnerId == ownerId)
                    .Select(contact => contact.Name)
                    .SingleOrDefault(),
                interaction.Type,
                interaction.OccurredAt,
                interaction.IsEmployerResponse,
                interaction.Notes))
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<WorkflowTaskItem>> GetTasksAsync(
        Guid applicationId,
        string ownerId,
        CancellationToken cancellationToken) =>
        await database.Tasks
            .AsNoTracking()
            .Where(task =>
                task.JobApplicationId == applicationId
                && task.OwnerId == ownerId)
            .OrderBy(task => task.CompletedAt != null)
            .ThenBy(task => task.DueAt == null)
            .ThenBy(task => task.DueAt)
            .ThenBy(task => task.Title)
            .Select(task => new WorkflowTaskItem(
                task.Id,
                task.Title,
                task.DueAt,
                task.CompletedAt,
                task.Notes))
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<WorkflowAppointmentItem>> GetAppointmentsAsync(
        Guid applicationId,
        string ownerId,
        CancellationToken cancellationToken) =>
        await database.Appointments
            .AsNoTracking()
            .Where(appointment =>
                appointment.JobApplicationId == applicationId
                && appointment.OwnerId == ownerId)
            .OrderBy(appointment => appointment.StartsAt)
            .Select(appointment => new WorkflowAppointmentItem(
                appointment.Id,
                appointment.Type,
                appointment.StartsAt,
                appointment.EndsAt,
                appointment.TimeZoneId,
                appointment.LocationOrLink,
                appointment.Notes))
            .ToListAsync(cancellationToken);

    private static ApplicationDetails ToApplicationDetails(
        JobApplication application,
        string? companyName) => new(
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
        application.DeletionScheduledAt);

    private static bool CanConfirmGhosted(
        JobApplication application,
        DateOnly localToday,
        DateTimeOffset? firstEmployerResponseAt) =>
        localToday >= application.AppliedOn.AddDays(ApplicationRules.GhostingConfirmationAfterDays)
        && firstEmployerResponseAt is null
        && application.Outcome != ApplicationOutcome.Ghosted;

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
