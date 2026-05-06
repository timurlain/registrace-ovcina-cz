using Microsoft.Extensions.Logging.Abstractions;
using RegistraceOvcina.Web.Features.Feedback;

namespace RegistraceOvcina.Web.Tests.Features.Feedback;

public sealed class UnconfiguredFeedbackEmailSenderTests
{
    private static UnconfiguredFeedbackEmailSender CreateSender() =>
        new(NullLogger<UnconfiguredFeedbackEmailSender>.Instance);

    [Fact]
    public async Task SendAsync_throws_with_configuration_hint()
    {
        var sender = CreateSender();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendAsync("rodic@example.com", "subject", "<p>body</p>", CancellationToken.None));

        Assert.Contains("not configured", ex.Message);
        Assert.Contains("Email:SharedMailboxAddress", ex.Message);
    }

    [Fact]
    public async Task SendAsync_throws_for_individual_call_too()
    {
        // A second smoke pass to exercise the same path with different inputs — confirms
        // the sender is uniformly fail-fast regardless of recipient.
        var sender = CreateSender();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendAsync("petr@example.com", "Připomínka", "<p>hi</p>", CancellationToken.None));
    }
}
