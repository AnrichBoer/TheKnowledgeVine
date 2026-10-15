using System.Security.Claims;
using KnowledgeVineAPI.Data;
using KnowledgeVineAPI.Models.Portal;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace KnowledgeVineAPI.Services.Portal;

public static class PortalAuthentication
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);
    public static readonly string[] Roles = [PortalRoles.Admin, PortalRoles.Teacher, PortalRoles.Parent];

    public static void AddPortalAuthentication(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme, options =>
            {
                options.Cookie.Name = environment.IsDevelopment() ? "KnowledgeVine.Portal" : "__Host-KnowledgeVine.Portal";
                options.Cookie.HttpOnly = true;
                options.Cookie.Path = "/";
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = IdleTimeout;
                options.SlidingExpiration = false; // Only explicit user activity renews a session.
                options.EventsType = typeof(PortalCookieEvents);
            });
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<TimeProvider>((options, clock) => options.TimeProvider = clock);
        services.AddScoped<PortalCookieEvents>();
        services.AddAuthorization(options =>
        {
            foreach (var role in Roles)
                options.AddPolicy("Portal" + role, policy => policy.RequireAuthenticatedUser().RequireRole(role));
        });
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = environment.IsDevelopment() ? "KnowledgeVine.Antiforgery" : "__Host-KnowledgeVine.Antiforgery";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
    }
}

public sealed class PortalCookieEvents(UserManager<PortalUser> users) : CookieAuthenticationEvents
{
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var user = await users.GetUserAsync(context.Principal!);
        var stamp = context.Principal?.FindFirstValue("AspNet.Identity.SecurityStamp");
        var valid = user is { IsActive: true, EmailConfirmed: true } && !string.IsNullOrEmpty(user.PasswordHash)
            && !string.IsNullOrEmpty(user.SecurityStamp) && stamp == user.SecurityStamp;
        if (valid)
        {
            var currentRoles = (await users.GetRolesAsync(user!)).Order().ToArray();
            var cookieRoles = context.Principal!.FindAll(ClaimTypes.Role).Select(x => x.Value).Order().ToArray();
            valid = currentRoles.SequenceEqual(cookieRoles) && currentRoles.Intersect(PortalAuthentication.Roles).Any();
        }
        if (!valid)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    }
}
