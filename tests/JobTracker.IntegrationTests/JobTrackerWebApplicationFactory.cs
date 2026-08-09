using JobTracker.Web.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using JobTracker.Web.Extraction;
using System.Collections.Concurrent;

namespace JobTracker.IntegrationTests;

public sealed class JobTrackerWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string databaseName = $"jobtracker-tests-{Guid.NewGuid()}";

    public StubJobPostingFetcher JobPostingFetcher { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<ApplicationDbContext>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));
            services.RemoveAll<IJobPostingFetcher>();
            services.AddSingleton<IJobPostingFetcher>(JobPostingFetcher);
        });
    }

    public sealed class StubJobPostingFetcher : IJobPostingFetcher
    {
        private readonly ConcurrentDictionary<string, JobPostingFetchResult> results =
            new(StringComparer.Ordinal);

        public void AddSuccess(string url, string html, string? finalUrl = null)
        {
            var requested = new Uri(url);
            results[url] = new JobPostingFetchResult(
                JobPostingImportFailure.None,
                requested,
                finalUrl is null ? requested : new Uri(finalUrl),
                html);
        }

        public void AddFailure(string url, JobPostingImportFailure failure) =>
            results[url] = JobPostingFetchResult.Failed(failure);

        public Task<JobPostingFetchResult> FetchAsync(
            string sourceUrl,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                results.TryRemove(sourceUrl, out var result)
                    ? result
                    : JobPostingFetchResult.Failed(JobPostingImportFailure.RequestFailed));
    }
}
