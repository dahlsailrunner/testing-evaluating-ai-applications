using System.Collections.Concurrent;
using CarvedRock.Data.Entities;
using CarvedRock.Domain;

namespace CarvedRock.ApiTests.Utils;

/// <summary>
/// Stands in for the MailKit-based <c>EmailService</c> in API tests: there is no SMTP server
/// here (the full-stack AppTests use a real MailPit container for that), so this fake just
/// records what would have been sent. That keeps the tests free of infrastructure while still
/// letting them assert "a cancellation email was sent for order N".
/// </summary>
public class RecordingOrderEmailSender : IOrderEmailSender
{
    public record SentEmail(string Kind, int OrderId, string To);

    private readonly ConcurrentBag<SentEmail> _sent = [];

    public IReadOnlyCollection<SentEmail> Sent => _sent;

    public Task SendOrderConfirmationAsync(Order order)
    {
        _sent.Add(new SentEmail("confirmation", order.Id, order.Email));
        return Task.CompletedTask;
    }

    public Task SendOrderCancellationAsync(Order order)
    {
        _sent.Add(new SentEmail("cancellation", order.Id, order.Email));
        return Task.CompletedTask;
    }
}
