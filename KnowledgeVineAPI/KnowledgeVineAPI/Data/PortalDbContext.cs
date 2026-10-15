using System.Text.RegularExpressions;
using KnowledgeVineAPI.Models.Portal;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace KnowledgeVineAPI.Data;

public sealed class PortalDbContext(DbContextOptions<PortalDbContext> options)
    : IdentityDbContext<PortalUser, IdentityRole<Guid>, Guid>(options)
{
    // Keep runtime and design-time models identical, without enabling optional passkey storage.
    protected override Version SchemaVersion => IdentitySchemaVersions.Version2;

    public DbSet<Student> Students => Set<Student>();
    public DbSet<ParentStudent> ParentStudents => Set<ParentStudent>();
    public DbSet<DailyUpdate> DailyUpdates => Set<DailyUpdate>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<PortalUser>(b =>
        {
            b.ToTable("users", t => t.HasCheckConstraint("ck_users_names", "first_name ~ '[^[:space:]]' AND last_name ~ '[^[:space:]]'"));
            b.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            b.Property(x => x.LastName).HasMaxLength(100).IsRequired();
            b.Property(x => x.Email).IsRequired();
            b.Property(x => x.PhoneNumber).HasColumnType("text");
            b.Property(x => x.IsActive).HasDefaultValue(true);
            b.HasIndex(x => x.NormalizedEmail).IsUnique().HasDatabaseName("ux_users_normalized_email");
            ConfigureTimestamps(b.Property(x => x.CreatedAt), b.Property(x => x.UpdatedAt));
        });
        builder.Entity<IdentityRole<Guid>>(b =>
        {
            b.ToTable("roles");
            b.HasData(Role(PortalRoles.AdminId, PortalRoles.Admin),
                Role(PortalRoles.TeacherId, PortalRoles.Teacher), Role(PortalRoles.ParentId, PortalRoles.Parent));
        });
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
        builder.Entity<IdentityUserLogin<Guid>>(b =>
        {
            b.ToTable("user_logins");
            b.Property(x => x.LoginProvider).HasMaxLength(256);
            b.Property(x => x.ProviderKey).HasMaxLength(256);
        });
        builder.Entity<IdentityUserToken<Guid>>(b =>
        {
            b.ToTable("user_tokens");
            b.Property(x => x.LoginProvider).HasMaxLength(256);
            b.Property(x => x.Name).HasMaxLength(256);
        });
        builder.Entity<Student>(b =>
        {
            b.ToTable("students", t => t.HasCheckConstraint("ck_students_names", "first_name ~ '[^[:space:]]' AND last_name ~ '[^[:space:]]'"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            b.Property(x => x.LastName).HasMaxLength(100).IsRequired();
            b.Property(x => x.IsActive).HasDefaultValue(true);
            b.HasOne<PortalUser>().WithMany().HasForeignKey(x => x.TeacherUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.TeacherUserId);
            ConfigureTimestamps(b.Property(x => x.CreatedAt), b.Property(x => x.UpdatedAt));
        });
        builder.Entity<ParentStudent>(b =>
        {
            b.ToTable("parent_students");
            b.HasKey(x => new { x.ParentUserId, x.StudentId });
            b.HasOne<PortalUser>().WithMany().HasForeignKey(x => x.ParentUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<PortalUser>().WithMany().HasForeignKey(x => x.LinkedByUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.StudentId);
            b.HasIndex(x => x.LinkedByUserId);
            b.Property(x => x.CreatedAt).HasDefaultValueSql("clock_timestamp()");
        });
        builder.Entity<DailyUpdate>(b =>
        {
            b.ToTable("daily_updates", t =>
            {
                t.HasCheckConstraint("ck_daily_updates_status", "status IN ('draft', 'shared')");
                t.HasCheckConstraint("ck_daily_updates_note", "char_length(note_text) <= 2000 AND (status = 'draft' OR note_text ~ '[^[:space:]]')");
                t.HasCheckConstraint("ck_daily_updates_shared_at", "(status = 'draft' AND shared_at IS NULL) OR (status = 'shared' AND shared_at IS NOT NULL)");
                t.HasCheckConstraint("ck_daily_updates_version", "version > 0");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.NoteText).HasColumnType("text").IsRequired();
            b.Property(x => x.Status).HasMaxLength(16).HasDefaultValue(DailyUpdateStatuses.Draft).IsRequired();
            b.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<PortalUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<PortalUser>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.StudentId, x.UpdateDate }).IsUnique().HasDatabaseName("ux_daily_updates_student_date");
            b.HasIndex(x => x.CreatedByUserId);
            b.HasIndex(x => x.UpdatedByUserId);
            ConfigureTimestamps(b.Property(x => x.CreatedAt), b.Property(x => x.UpdatedAt));
            b.Property(x => x.SharedAt).ValueGeneratedOnAddOrUpdate();
            var version = b.Property(x => x.Version).HasDefaultValue(1).IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
            version.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
            version.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        });

        foreach (var entity in builder.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
    }

    private static IdentityRole<Guid> Role(Guid id, string name) => new()
    {
        Id = id, Name = name, NormalizedName = name.ToUpperInvariant(), ConcurrencyStamp = id.ToString()
    };

    private static void ConfigureTimestamps(
        Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<DateTime> created,
        Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<DateTime> updated)
    {
        created.HasDefaultValueSql("clock_timestamp()");
        updated.HasDefaultValueSql("clock_timestamp()").ValueGeneratedOnAddOrUpdate();
        updated.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        updated.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
    }
}
