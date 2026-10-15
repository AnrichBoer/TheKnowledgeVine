
using KnowledgeVineAPI.Services;
using KnowledgeVineAPI.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using KnowledgeVineAPI.Services.Mail;

namespace KnowledgeVineAPI
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var bootstrapAdmin = args.Contains("--bootstrap-admin");
            var builder = WebApplication.CreateBuilder(bootstrapAdmin ? [] : args);
            if (bootstrapAdmin) builder.Logging.ClearProviders();

            if (args.Contains("--portal-migrate"))
            {
                if (!builder.Environment.IsDevelopment())
                    throw new InvalidOperationException("The local migration command requires Development environment.");
                var connection = builder.Configuration.GetConnectionString("KnowledgeVineMigrations")
                    ?? throw new InvalidOperationException("Configure the KnowledgeVine migration connection in user secrets.");
                PortalDatabaseTarget.Validate(connection, localOnly: true);
                await using var database = new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>()
                    .UseNpgsql(connection).Options);
                await database.Database.MigrateAsync();
                Console.WriteLine("KnowledgeVine local migrations applied.");
                return 0;
            }

            // Add services to the container.

            // MVC's antiforgery authorization filters are registered by the view-services builder.
            builder.Services.AddControllersWithViews();

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            builder.Services.AddScoped<IEmailService, EmailService>();
            builder.Services.AddScoped<IEmailTransport, SmtpEmailTransport>();
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                foreach (var address in builder.Configuration.GetSection("Portal:TrustedProxyAddresses").Get<string[]>() ?? [])
                    options.KnownProxies.Add(IPAddress.Parse(address));
            });
            var portalConfigured = builder.Services.AddPortalData(builder.Configuration, builder.Environment);
            if (bootstrapAdmin && !portalConfigured)
            {
                Console.Error.WriteLine("Configure the KnowledgeVine connection before admin bootstrap.");
                return 1;
            }
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = 429;
                options.AddPolicy("portal-account", context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20, Window = TimeSpan.FromMinutes(5), QueueLimit = 0, AutoReplenishment = true
                    }));
            });

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAngular",
                    policy => policy
                        .WithOrigins("http://localhost:4200",
                        "https://www.knowledgevinemontessori.co.za",
                        "https://knowledgevinemontessori.co.za")
                        .AllowAnyHeader()
                        .AllowAnyMethod());
            });

            await using var app = builder.Build();
            if (bootstrapAdmin) return await PortalAdminBootstrapCommand.RunAsync(app.Services, args);

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
                if (portalConfigured)
                    app.MapGet("/health/database", async (PortalDbContext database, CancellationToken cancellationToken) =>
                    {
                        if (!await database.Database.CanConnectAsync(cancellationToken))
                            return Results.Problem("KnowledgeVine database is unavailable.", statusCode: 503);
                        var pending = await database.Database.GetPendingMigrationsAsync(cancellationToken);
                        return pending.Any()
                            ? Results.Problem("KnowledgeVine schema migrations are pending.", statusCode: 503)
                            : Results.Ok(new { status = "ready", database = "knowledgevine_local" });
                    });
            }

            app.UseForwardedHeaders();
            app.UseHttpsRedirection();
            app.UseCors("AllowAngular");
            app.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments("/portal"))
                {
                    context.Response.Headers.CacheControl = "no-store";
                    context.Response.Headers.Pragma = "no-cache";
                    context.Response.Headers["Referrer-Policy"] = "no-referrer";
                    if (!portalConfigured)
                    {
                        context.Response.StatusCode = 503;
                        await context.Response.WriteAsJsonAsync(new { message = "The portal is not available yet." });
                        return;
                    }
                }
                await next();
            });
            if (portalConfigured)
            {
                app.UseAuthentication();
                app.UseAuthorization();
            }
            app.UseRateLimiter();

            app.MapControllers();

            await app.RunAsync();
            return 0;
        }
    }
}
