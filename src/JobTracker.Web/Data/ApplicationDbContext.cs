using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Web.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<JobApplication> JobApplications => Set<JobApplication>();
    public DbSet<StatusHistory> StatusHistory => Set<StatusHistory>();
    public DbSet<ExtractionDraft> ExtractionDrafts => Set<ExtractionDraft>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Interaction> Interactions => Set<Interaction>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<Invitation> Invitations => Set<Invitation>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.DisplayName).HasMaxLength(120).IsRequired();
            entity.Property(user => user.TimeZoneId).HasMaxLength(100).IsRequired();
        });

        builder.Entity<Invitation>(entity =>
        {
            entity.HasKey(invitation => invitation.Id);
            entity.Property(invitation => invitation.CodeHash).HasMaxLength(64).IsRequired();
            entity.Property(invitation => invitation.ConcurrencyStamp).IsConcurrencyToken();
            entity.Property(invitation => invitation.UsedByUserId).HasMaxLength(450);
            entity.HasIndex(invitation => invitation.CodeHash).IsUnique();
            entity.HasIndex(invitation => invitation.ExpiresAt);
            entity.HasOne(invitation => invitation.UsedByUser)
                .WithMany()
                .HasForeignKey(invitation => invitation.UsedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureOwnedEntity<Company>(builder);
        ConfigureOwnedEntity<JobApplication>(builder);
        ConfigureOwnedEntity<StatusHistory>(builder);
        ConfigureOwnedEntity<ExtractionDraft>(builder);
        ConfigureOwnedEntity<Contact>(builder);
        ConfigureOwnedEntity<Interaction>(builder);
        ConfigureOwnedEntity<TaskItem>(builder);
        ConfigureOwnedEntity<Appointment>(builder);

        builder.Entity<Company>(entity =>
        {
            entity.Property(company => company.Name).HasMaxLength(200).IsRequired();
            entity.Property(company => company.Website).HasMaxLength(2048);
            entity.Property(company => company.Location).HasMaxLength(300);
            entity.HasIndex(company => new { company.OwnerId, company.Name });
        });

        builder.Entity<JobApplication>(entity =>
        {
            entity.Property(application => application.RoleTitle).HasMaxLength(200).IsRequired();
            entity.Property(application => application.SourceUrl).HasMaxLength(2048);
            entity.Property(application => application.Stage).HasConversion<string>().HasMaxLength(40);
            entity.Property(application => application.Outcome).HasConversion<string>().HasMaxLength(40);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(application => new { application.CompanyId, application.OwnerId })
                .HasPrincipalKey(company => new { company.Id, company.OwnerId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(application => new { application.OwnerId, application.AppliedOn });
        });

        builder.Entity<ExtractionDraft>(entity =>
        {
            entity.Property(draft => draft.SourceType).HasConversion<string>().HasMaxLength(30);
            entity.Property(draft => draft.ParsedFieldsJson).IsRequired();
            entity.Property(draft => draft.EvidenceJson).IsRequired();
            entity.Property(draft => draft.WarningsJson).IsRequired();
            entity.HasIndex(draft => new { draft.OwnerId, draft.ExpiresAt });
        });

        builder.Entity<StatusHistory>(entity =>
        {
            entity.Property(history => history.PreviousStage).HasConversion<string>().HasMaxLength(40);
            entity.Property(history => history.NewStage).HasConversion<string>().HasMaxLength(40);
            entity.Property(history => history.PreviousOutcome).HasConversion<string>().HasMaxLength(40);
            entity.Property(history => history.NewOutcome).HasConversion<string>().HasMaxLength(40);
            ConfigureApplicationRelationship(entity, history => new { history.JobApplicationId, history.OwnerId });
        });

        builder.Entity<Contact>(entity =>
        {
            entity.Property(contact => contact.Name).HasMaxLength(200).IsRequired();
            entity.Property(contact => contact.JobTitle).HasMaxLength(200);
            entity.Property(contact => contact.Email).HasMaxLength(320);
            entity.Property(contact => contact.Phone).HasMaxLength(50);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(contact => new { contact.CompanyId, contact.OwnerId })
                .HasPrincipalKey(company => new { company.Id, company.OwnerId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JobApplication>()
                .WithMany()
                .HasForeignKey(contact => new { contact.JobApplicationId, contact.OwnerId })
                .HasPrincipalKey(application => new { application.Id, application.OwnerId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Interaction>(entity =>
        {
            entity.Property(interaction => interaction.Type).HasConversion<string>().HasMaxLength(30);
            ConfigureApplicationRelationship(entity, interaction => new { interaction.JobApplicationId, interaction.OwnerId });
            entity.HasOne<Contact>()
                .WithMany()
                .HasForeignKey(interaction => new { interaction.ContactId, interaction.OwnerId })
                .HasPrincipalKey(contact => new { contact.Id, contact.OwnerId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<TaskItem>(entity =>
        {
            entity.Property(task => task.Title).HasMaxLength(240).IsRequired();
            ConfigureApplicationRelationship(entity, task => new { task.JobApplicationId, task.OwnerId });
        });

        builder.Entity<Appointment>(entity =>
        {
            entity.Property(appointment => appointment.Type).HasConversion<string>().HasMaxLength(30);
            entity.Property(appointment => appointment.TimeZoneId).HasMaxLength(100).IsRequired();
            entity.Property(appointment => appointment.LocationOrLink).HasMaxLength(2048);
            ConfigureApplicationRelationship(entity, appointment => new { appointment.JobApplicationId, appointment.OwnerId });
        });
    }

    private static void ConfigureOwnedEntity<TEntity>(ModelBuilder builder)
        where TEntity : OwnedEntity
    {
        builder.Entity<TEntity>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasAlternateKey(item => new { item.Id, item.OwnerId });
            entity.Property(item => item.OwnerId).HasMaxLength(450).IsRequired();
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(item => item.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureApplicationRelationship<TEntity>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity,
        System.Linq.Expressions.Expression<Func<TEntity, object?>> foreignKey)
        where TEntity : OwnedEntity
    {
        entity.HasOne<JobApplication>()
            .WithMany()
            .HasForeignKey(foreignKey)
            .HasPrincipalKey(application => new { application.Id, application.OwnerId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
