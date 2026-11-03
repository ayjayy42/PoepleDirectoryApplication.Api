using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PoepleDirectoryApplication.Api.Models;
using PoepleDirectoryApplication.Api.Providers;
using Xunit;

namespace PoepleDirectoryApplication.Api.Tests;

public sealed class EmailApiTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task MissingOrInvalidKey_IsUnauthorizedWithoutSending(string? key)
    {
        await using var factory = new EmailWebFactory();
        using var client = factory.CreateApiClient(key);

        using var response = await client.PostAsJsonAsync("/api/email", new { subject = "Verification", body = "Body" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.Provider.Sent);
    }

    [Fact]
    public async Task MultipleApiKeyHeaderValues_AreRejected()
    {
        await using var factory = new EmailWebFactory();
        using var client = factory.CreateApiClient(null);
        client.DefaultRequestHeaders.Add("X-Api-Key", new[] { EmailWebFactory.ApiKey, EmailWebFactory.ApiKey });

        using var response = await client.PostAsJsonAsync("/api/email", new { subject = "Verification", body = "Body" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.Provider.Sent);
    }

    [Fact]
    public async Task ValidRequest_PassesTrimmedSubjectAndUnchangedBodyToProvider()
    {
        await using var factory = new EmailWebFactory();
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync("/api/email", new { subject = "  Verification  ", body = "Body\nsecond line" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(factory.Provider.Sent);
        Assert.Equal("Verification", sent.EmailSubject);
        Assert.Equal("Body\nsecond line", sent.EmailBody);
    }

    public static IEnumerable<object[]> InvalidRequests()
    {
        yield return new object[] { "", "Body" };
        yield return new object[] { "   ", "Body" };
        yield return new object[] { "Subject", "" };
        yield return new object[] { "Subject", "   " };
        yield return new object[] { new string('s', 201), "Body" };
        yield return new object[] { "Subject", new string('b', 10001) };
        yield return new object[] { "Header\r\nInjected: yes", "Body" };
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task InvalidRequest_IsBadRequestWithoutSending(string subject, string body)
    {
        await using var factory = new EmailWebFactory();
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync("/api/email", new { subject, body });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Provider.Sent);
    }

    [Fact]
    public async Task SmtpFailure_ReturnsSanitizedBadGatewayProblem()
    {
        await using var factory = new EmailWebFactory();
        factory.Provider.Failure = new SmtpException("smtp-sensitive-value-must-not-leak");
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync("/api/email", new { subject = "Verification", body = "Body" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Email delivery failed.", body);
        Assert.DoesNotContain("smtp-sensitive-value-must-not-leak", body);
    }

    [Fact]
    public async Task ProviderDeadline_ReturnsServiceUnavailableWithoutLeakingDetails()
    {
        await using var factory = new EmailWebFactory();
        factory.Provider.Failure = new OperationCanceledException("timeout-sensitive-value-must-not-leak");
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync("/api/email", new { subject = "Verification", body = "Body" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Email delivery timed out.", body);
        Assert.DoesNotContain("timeout-sensitive-value-must-not-leak", body);
    }

    [Fact]
    public async Task UnexpectedProviderFailure_ReturnsSanitizedServerError()
    {
        await using var factory = new EmailWebFactory();
        factory.Provider.Failure = new InvalidOperationException("internal-sensitive-value-must-not-leak");
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync("/api/email", new { subject = "Verification", body = "Body" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("internal-sensitive-value-must-not-leak", body);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task RequestLimit_RejectsThirtyFirstRequestWithoutAnotherSend()
    {
        await using var factory = new EmailWebFactory();
        using var client = factory.CreateApiClient();
        for (var request = 0; request < 30; request++)
        {
            using var accepted = await client.PostAsJsonAsync("/api/email", new { subject = "Verification", body = "Body" });
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }

        using var limited = await client.PostAsJsonAsync("/api/email", new { subject = "Verification", body = "Body" });

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(30, factory.Provider.Sent.Count);
    }

    [Fact]
    public async Task RealProvider_StalledLoopbackSmtpIsCanceledByConfiguredDeadline()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var provider = new EmailProvider(Options.Create(new MailSettings
            {
                SmtpServer = "127.0.0.1", SmtpPort = port, EnableSsl = false,
                FromAddress = "sender@example.test", ToAddress = "recipient@example.test",
                SendTimeoutSeconds = 1,
            }));
            var acceptedConnection = listener.AcceptTcpClientAsync();
            var send = provider.SendMailAsync(new MailData { EmailSubject = "Verification", EmailBody = "Body" });
            using var connection = await acceptedConnection.WaitAsync(TimeSpan.FromSeconds(5));

            // The local fixture never sends an SMTP greeting or accepts message data.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await send.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ProductionProvider_CanBeResolvedFromRealApplicationRegistration()
    {
        await using var factory = new EmailWebFactory(useFakeProvider: false);
        using var client = factory.CreateApiClient();
        await using var scope = factory.Services.CreateAsyncScope();

        Assert.IsType<EmailProvider>(scope.ServiceProvider.GetRequiredService<IEmailProvider>());
    }

    [Theory]
    [InlineData("EmailApi:ApiKey", "", "Configure EmailApi:ApiKey")]
    [InlineData("EmailApi:ApiKey", "short", "Configure EmailApi:ApiKey")]
    [InlineData("MailSettings:SmtpPort", "0", "SmtpPort")]
    [InlineData("MailSettings:SendTimeoutSeconds", "0", "SendTimeoutSeconds")]
    [InlineData("MailSettings:ToAddress", "invalid", "ToAddress")]
    public async Task InvalidConfiguration_FailsStartup(string key, string value, string expectedMessage)
    {
        await using var factory = new EmailWebFactory(overrides: new Dictionary<string, string?> { [key] = value });

        var failure = Record.Exception(() => factory.CreateApiClient());

        Assert.NotNull(failure);
        Assert.Contains("OptionsValidationException", failure.ToString());
        Assert.Contains(expectedMessage, failure.ToString());
    }
}

internal sealed class EmailWebFactory(bool useFakeProvider = true, Dictionary<string, string?>? overrides = null)
    : WebApplicationFactory<Program>
{
    internal const string ApiKey = "test-only-api-key-at-least-32-characters-long";
    public RecordingEmailProvider Provider { get; } = new();

    public HttpClient CreateApiClient(string? key = ApiKey)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        if (key is not null) client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["EmailApi:ApiKey"] = ApiKey,
                ["MailSettings:SmtpServer"] = "127.0.0.1",
                ["MailSettings:SmtpPort"] = "1",
                ["MailSettings:ToAddress"] = "recipient@example.test",
                ["MailSettings:FromAddress"] = "sender@example.test",
                ["MailSettings:SendTimeoutSeconds"] = "1",
            };
            if (overrides is not null)
                foreach (var entry in overrides) settings[entry.Key] = entry.Value;
            config.AddInMemoryCollection(settings);
        });
        if (useFakeProvider)
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmailProvider>();
                services.AddSingleton<IEmailProvider>(Provider);
            });
    }
}

internal sealed class RecordingEmailProvider : IEmailProvider
{
    public List<MailData> Sent { get; } = new();
    public Exception? Failure { get; set; }

    public Task SendMailAsync(MailData mailData, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Failure is not null) return Task.FromException(Failure);
        Sent.Add(mailData);
        return Task.CompletedTask;
    }
}

