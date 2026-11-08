using System.Net.Mail;
using System.Text;
using CarvedRock.Data.Entities;
using CarvedRock.Domain;
using MailKit.Client;
using MimeKit;

namespace CarvedRock.Api;

public class EmailService(MailKitClientFactory factory, ILogger<EmailService> logger)
    : IOrderEmailSender
{
    public Task SendOrderConfirmationAsync(Order order) =>
        SendAsync(order, "Your CarvedRock Order",
            "<h1>Thank you for your order!</h1>", "Enjoy your new gear!", "Order confirmation");

    public Task SendOrderCancellationAsync(Order order) =>
        SendAsync(order, "Your CarvedRock Order Was Cancelled",
            $"<h1>Your order #{order.Id} has been cancelled.</h1>",
            "If you didn't request this cancellation, please contact us.", "Order cancellation");

    private async Task SendAsync(Order order, string subject, string narrative,
        string additionalNotes, string kind)
    {
        string basePath = AppContext.BaseDirectory;
        string templatePath = Path.Combine(basePath, "emailTemplate.html");
        string template = await File.ReadAllTextAsync(templatePath);

        template = template.Replace("{{NarrativeContent}}", narrative);

        var productRows = new StringBuilder();
        foreach (var detail in order.Details)
        {
            productRows.AppendLine(
                $"<tr><td>{detail.ProductName}</td><td>{detail.Quantity}</td><td>{detail.LineTotal}</td></tr>");
        }
        template = template.Replace("{{ProductRows}}", productRows.ToString());
        template = template.Replace("{{AdditionalNotes}}", additionalNotes);

        var client = await factory.GetSmtpClientAsync();

        using var message = new MailMessage
        {
            Body = template,
            Subject = subject,
            IsBodyHtml = true,
            From = new MailAddress("e-commerce@carvedrock.com", "Carved Rock Shop"),
            To = { order.Email }
        };

        await client.SendAsync(MimeMessage.CreateFromMailMessage(message));
        logger.LogInformation("{Kind} email sent to {Email} for order {OrderId}.",
            kind, order.Email, order.Id);
    }
}
