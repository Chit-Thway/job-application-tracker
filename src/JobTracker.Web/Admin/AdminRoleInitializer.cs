using JobTracker.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace JobTracker.Web.Admin;

public sealed class AdminRoleInitializer(
    RoleManager<IdentityRole> roleManager,
    UserManager<ApplicationUser> userManager,
    IOptions<AdminOptions> options,
    IHostEnvironment environment,
    IConfiguration configuration,
    ILogger<AdminRoleInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!await roleManager.RoleExistsAsync(AdminRole.Name))
        {
            var roleResult = await roleManager.CreateAsync(new IdentityRole(AdminRole.Name));
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException("The administrator role could not be initialized.");
            }
        }

        var bootstrapEmail = options.Value.BootstrapEmail.Trim();
        if (bootstrapEmail.Length == 0 && environment.IsDevelopment())
        {
            bootstrapEmail = configuration["BootstrapAccount:Email"]?.Trim() ?? string.Empty;
        }
        if (bootstrapEmail.Length == 0)
        {
            return;
        }

        var user = await userManager.FindByEmailAsync(bootstrapEmail);
        if (user is null || await userManager.IsInRoleAsync(user, AdminRole.Name))
        {
            return;
        }

        var result = await userManager.AddToRoleAsync(user, AdminRole.Name);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("The configured administrator could not be assigned.");
        }

        logger.LogInformation("The configured bootstrap administrator role was assigned.");
    }
}
