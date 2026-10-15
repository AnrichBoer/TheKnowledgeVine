using KnowledgeVineAPI.Models.Portal;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using KnowledgeVineAPI.Services.Portal;

namespace KnowledgeVineAPI.Data;

public static class PortalDataServices
{
    public static bool AddPortalData(this IServiceCollection services, IConfiguration configuration,
        IHostEnvironment environment)
    {
        var connection = configuration.GetConnectionString("KnowledgeVine");
        if (string.IsNullOrWhiteSpace(connection)) return false;
        // Local tools always require loopback. A development VM can explicitly use its
        // private Docker database; the KnowledgeVine database-name guard still applies.
        var localOnly = environment.IsDevelopment() && configuration.GetValue("Portal:LocalDatabaseOnly", true);
        PortalDatabaseTarget.Validate(connection, localOnly: localOnly);

        services.AddDbContext<PortalDbContext>(options => options.UseNpgsql(connection));
        services.AddIdentityCore<PortalUser>(ConfigureIdentityOptions)
            .AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<PortalDbContext>().AddDefaultTokenProviders()
            .AddSignInManager().AddTokenProvider<PortalSetupTokenProvider>(PortalAccountLinks.SetupProvider);
        services.AddScoped<PortalAdminBootstrap>();
        services.AddScoped<PortalAdminService>();
        services.AddScoped<PortalTeacherService>();
        services.AddScoped<PortalParentService>();
        services.AddScoped<PortalAccountLinks>();
        services.AddScoped<PortalMailTemplates>();
        services.AddScoped<PortalAccountPreviewMail>();
        services.AddScoped<IPortalAccountMail, PortalAccountMail>();
        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(24));
        services.AddPortalAuthentication(environment);

        var keyPath = configuration["Portal:DataProtectionKeyPath"];
        if (string.IsNullOrWhiteSpace(keyPath))
            throw new InvalidOperationException("Configure a persistent KnowledgeVine data-protection key directory.");
        Directory.CreateDirectory(keyPath);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        services.AddDataProtection().SetApplicationName("KnowledgeVinePortal")
            .PersistKeysToFileSystem(new DirectoryInfo(keyPath));
        return true;
    }

    public static void ConfigureIdentityOptions(IdentityOptions options)
    {
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version2;
        options.Stores.MaxLengthForKeys = 256;
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = true;
        options.Password.RequiredLength = 12;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    }
}
