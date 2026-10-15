using System.ComponentModel.DataAnnotations;
using KnowledgeVineAPI.Controllers;
using KnowledgeVineAPI.Models;
using KnowledgeVineAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeVineAPI.Tests;

public class EmailControllerTests
{
    private static EmailRequest ValidRequest() => new()
    {
        Name = "Parent", ContactNumber = "0613041993",
        Email = "parent@example.com", Message = "Can we arrange a visit?"
    };

    private static EmailController Controller(Func<Task<bool>> send) =>
        new(new FakeEmailService(send), NullLogger<EmailController>.Instance);

    [Fact]
    public async Task ReportsSuccessOnlyWhenSmtpAcceptsTheMessage()
    {
        var result = Assert.IsType<OkObjectResult>(await Controller(() => Task.FromResult(true)).SendEmail(ValidRequest()));
        Assert.Equal(200, result.StatusCode);
        Assert.Contains("\"success\":true", System.Text.Json.JsonSerializer.Serialize(result.Value));
    }

    [Fact]
    public async Task Returns503WhenTheEmailServiceReportsFailure()
    {
        var result = Assert.IsType<ObjectResult>(await Controller(() => Task.FromResult(false)).SendEmail(ValidRequest()));
        Assert.Equal(503, result.StatusCode);
        Assert.Contains("\"success\":false", System.Text.Json.JsonSerializer.Serialize(result.Value));
    }

    [Fact]
    public async Task DoesNotExposeInternalExceptionDetails()
    {
        var result = Assert.IsType<ObjectResult>(await Controller(() => throw new InvalidOperationException("private SMTP detail")).SendEmail(ValidRequest()));
        Assert.Equal(503, result.StatusCode);
        Assert.DoesNotContain("private SMTP detail", System.Text.Json.JsonSerializer.Serialize(result.Value));
    }

    [Theory]
    [InlineData("Name", "")]
    [InlineData("Name", "   ")]
    [InlineData("ContactNumber", "12")]
    [InlineData("Email", "not-an-email")]
    [InlineData("Message", "   ")]
    public void RejectsInvalidEnquiryFields(string field, string value)
    {
        var request = ValidRequest();
        typeof(EmailRequest).GetProperty(field)!.SetValue(request, value);
        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), [], true));
    }

    [Fact]
    public void RejectsOversizedMessages()
    {
        var request = ValidRequest(); request.Message = new string('x', 4001);
        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), [], true));
    }

    private sealed class FakeEmailService(Func<Task<bool>> send) : IEmailService
    {
        public Task<bool> SendEmailAsync(EmailRequest request) => send();
    }
}
