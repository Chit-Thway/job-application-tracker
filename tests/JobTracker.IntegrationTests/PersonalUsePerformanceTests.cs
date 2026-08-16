using System.Diagnostics;
using JobTracker.Web.Applications;
using JobTracker.Web.Data;
using JobTracker.Web.Identity;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.IntegrationTests;

public sealed class PersonalUsePerformanceTests
{
    [Fact]
    public async Task Dashboard_WithOneThousandApplications_CompletesWithinSmokeBudget()
    {
        await using var database = Database();
        var owner = new ApplicationUser
        {
            Id = "performance-owner",
            UserName = "performance-owner@example.test",
            Email = "performance-owner@example.test",
            DisplayName = "Performance Test User",
            TimeZoneId = "Australia/Perth",
        };
        database.Users.Add(owner);

        var applications = Enumerable.Range(0, 1_000)
            .Select(index => new JobApplication
            {
                OwnerId = owner.Id,
                RoleTitle = $"Synthetic performance role {index:D4}",
                AppliedOn = new DateOnly(2026, 6, 1).AddDays(index % 92),
                Stage = (PipelineStage)(index % Enum.GetValues<PipelineStage>().Length),
                Outcome = ApplicationOutcome.Active,
                IsSavedForever = index % 5 == 0,
            })
            .ToList();
        database.JobApplications.AddRange(applications);
        database.Interactions.AddRange(applications.Take(200).Select(application => new Interaction
        {
            OwnerId = owner.Id,
            JobApplicationId = application.Id,
            Type = InteractionType.Email,
            OccurredAt = new DateTimeOffset(
                application.AppliedOn.ToDateTime(new TimeOnly(12, 0)),
                TimeSpan.FromHours(8)).AddHours(1),
            IsEmployerResponse = true,
            Notes = "Synthetic performance response",
        }));
        database.Tasks.AddRange(applications.Take(100).Select(application => new TaskItem
        {
            OwnerId = owner.Id,
            JobApplicationId = application.Id,
            Title = "Synthetic performance follow-up",
            DueAt = new DateTimeOffset(2026, 8, 18, 4, 0, 0, TimeSpan.Zero),
        }));
        database.Appointments.AddRange(applications.Take(50).Select(application => new Appointment
        {
            OwnerId = owner.Id,
            JobApplicationId = application.Id,
            Type = AppointmentType.Interview,
            StartsAt = new DateTimeOffset(2026, 8, 20, 2, 0, 0, TimeSpan.Zero),
            EndsAt = new DateTimeOffset(2026, 8, 20, 3, 0, 0, TimeSpan.Zero),
            TimeZoneId = "Australia/Perth",
        }));
        await database.SaveChangesAsync();

        var service = new DashboardService(
            database,
            new FixedCurrentUser(owner.Id),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 16, 4, 0, 0, TimeSpan.Zero)));
        var stopwatch = Stopwatch.StartNew();

        var snapshot = await service.GetAsync();

        stopwatch.Stop();
        Assert.Equal(1_000, snapshot.ApplicationCount);
        Assert.Equal(200, snapshot.ResponseCount);
        Assert.Equal(50, snapshot.InterviewCount);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"The personal-use dashboard smoke query took {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
    }

    private static ApplicationDbContext Database()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"personal-use-performance-{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class FixedCurrentUser(string userId) : ICurrentUserContext
    {
        public string? UserId { get; } = userId;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
