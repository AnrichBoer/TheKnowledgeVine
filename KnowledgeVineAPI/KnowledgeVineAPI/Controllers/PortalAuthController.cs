using System.ComponentModel.DataAnnotations;
using KnowledgeVineAPI.Models.Portal;
using KnowledgeVineAPI.Services.Portal;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace KnowledgeVineAPI.Controllers;

public sealed record PortalLoginRequest([Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(128)] string Password);
public sealed record PortalRecoveryRequest([Required, EmailAddress, StringLength(256)] string Email);
public sealed record PortalPasswordRequest(Guid UserId, [Required, StringLength(4096)] string Token,
    [Required, StringLength(128, MinimumLength = 12)] string Password);
public sealed record PortalSession(Guid Id, string Name, string Email, string[] Roles, DateTimeOffset ExpiresAt, DateTimeOffset ServerTime);

[ApiController]
[Route("portal/auth")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PortalAuthController(UserManager<PortalUser> users, SignInManager<PortalUser> signIn,
    IAntiforgery antiforgery, PortalAccountLinks links, TimeProvider clock, IHostEnvironment environment,
    ILogger<PortalAuthController> logger) : ControllerBase
{
    [HttpGet("csrf")]
    [AllowAnonymous]
    public IActionResult Csrf()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false, Secure = !environment.IsDevelopment() || Request.IsHttps,
            SameSite = SameSiteMode.Strict, Path = "/", IsEssential = true
        });
        return Ok(new { ready = true });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("portal-account")]
    public async Task<IActionResult> Login(PortalLoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive || !user.EmailConfirmed || string.IsNullOrEmpty(user.PasswordHash)
            || user.TwoFactorEnabled || !(await users.GetRolesAsync(user)).Intersect(PortalAuthentication.Roles).Any())
            return Unauthorized(new { message = "Unable to sign in. Check your email and password, or complete your account setup." });
        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
            return Unauthorized(new { message = "Unable to sign in. Check your email and password, or try again later." });
        var expires = clock.GetUtcNow() + PortalAuthentication.IdleTimeout;
        await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, await signIn.CreateUserPrincipalAsync(user),
            Properties(expires));
        return Ok(await Session(user, expires));
    }

    [HttpGet("session")]
    [Authorize]
    public async Task<IActionResult> CurrentSession()
    {
        var ticket = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var user = await users.GetUserAsync(User);
        return Ok(await Session(user!, ticket.Properties!.ExpiresUtc!.Value));
    }

    [HttpPost("activity")]
    [Authorize]
    public async Task<IActionResult> Activity()
    {
        var user = await users.GetUserAsync(User);
        var expires = clock.GetUtcNow() + PortalAuthentication.IdleTimeout;
        await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, await signIn.CreateUserPrincipalAsync(user!), Properties(expires));
        return Ok(await Session(user!, expires));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        return NoContent();
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("portal-account")]
    public async Task<IActionResult> ForgotPassword(PortalRecoveryRequest request, CancellationToken cancellationToken)
    {
        if (!links.DeliveryAvailable)
            return StatusCode(503, new { message = "Password recovery is unavailable right now. Please contact the school." });
        try { await links.RequestRecoveryAsync(request.Email, cancellationToken); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning("Portal recovery delivery failed ({ErrorType})", error.GetType().Name);
            return StatusCode(503, new { message = "We could not process your request. Please try again later." });
        }
        return Ok(new { message = "If an active account uses this email, password instructions will be sent." });
    }

    [HttpPost("setup-password")]
    [AllowAnonymous]
    [EnableRateLimiting("portal-account")]
    public Task<IActionResult> SetupPassword(PortalPasswordRequest request, CancellationToken cancellationToken)
        => CompletePassword(request, true, cancellationToken);

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("portal-account")]
    public Task<IActionResult> ResetPassword(PortalPasswordRequest request, CancellationToken cancellationToken)
        => CompletePassword(request, false, cancellationToken);

    private async Task<IActionResult> CompletePassword(PortalPasswordRequest request, bool initial, CancellationToken cancellationToken)
    {
        var result = await links.SetPasswordAsync(request.UserId, request.Token, request.Password, initial, cancellationToken);
        if (result.Succeeded) await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        return result.Succeeded ? Ok(new { message = "Your password is set. Please sign in." })
            : BadRequest(new { message = result.Errors.Any(x => x.Code == "InvalidToken")
                ? "This link is invalid, expired or already used. Request a new link."
                : "Use at least 12 characters, with an uppercase letter, lowercase letter, number and symbol." });
    }

    private async Task<PortalSession> Session(PortalUser user, DateTimeOffset expires) =>
        new(user.Id, $"{user.FirstName} {user.LastName}", user.Email!,
            (await users.GetRolesAsync(user)).Where(PortalAuthentication.Roles.Contains).ToArray(), expires, clock.GetUtcNow());
    private AuthenticationProperties Properties(DateTimeOffset expires) => new()
    {
        IsPersistent = false, AllowRefresh = false, IssuedUtc = clock.GetUtcNow(), ExpiresUtc = expires
    };
}

[ApiController]
[Route("portal/access")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PortalAccessController : ControllerBase
{
    [HttpGet("admin"), Authorize(Policy = "PortalAdmin")]
    public IActionResult Admin() => Ok(new { role = "Admin" });
    [HttpGet("teacher"), Authorize(Policy = "PortalTeacher")]
    public IActionResult Teacher() => Ok(new { role = "Teacher" });
    [HttpGet("parent"), Authorize(Policy = "PortalParent")]
    public IActionResult Parent() => Ok(new { role = "Parent" });
}
