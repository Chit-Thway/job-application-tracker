using JobTracker.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Web.Identity;

public sealed class OwnedApplicationService(
    ApplicationDbContext database,
    ICurrentUserContext currentUser)
{
    public async Task<JobApplication?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        return await database.JobApplications
            .SingleOrDefaultAsync(
                application => application.Id == id && application.OwnerId == ownerId,
                cancellationToken);
    }

    public async Task<bool> RenameAsync(
        Guid id,
        string roleTitle,
        CancellationToken cancellationToken = default)
    {
        var application = await FindAsync(id, cancellationToken);
        if (application is null)
        {
            return false;
        }

        application.RoleTitle = roleTitle;
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var application = await FindAsync(id, cancellationToken);
        if (application is null)
        {
            return false;
        }

        database.JobApplications.Remove(application);
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<TaskItem?> AddTaskAsync(
        Guid applicationId,
        string title,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var applicationExists = await database.JobApplications.AnyAsync(
            application => application.Id == applicationId && application.OwnerId == ownerId,
            cancellationToken);

        if (!applicationExists)
        {
            return null;
        }

        var task = new TaskItem
        {
            OwnerId = ownerId,
            JobApplicationId = applicationId,
            Title = title,
        };

        database.Tasks.Add(task);
        await database.SaveChangesAsync(cancellationToken);
        return task;
    }

    private string RequireOwnerId()
    {
        return currentUser.UserId
            ?? throw new InvalidOperationException("An authenticated user is required.");
    }
}
