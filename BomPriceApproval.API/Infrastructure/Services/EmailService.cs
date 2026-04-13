using MailKit.Net.Smtp;
using MimeKit;

namespace BomPriceApproval.API.Infrastructure.Services;

public class EmailService(IConfiguration config, ILogger<EmailService> logger)
{
    public async Task SendAsync(string toEmail, string toName, string subject, string htmlBody, byte[]? attachment = null, string? attachmentName = null)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(config["Email:FromName"], config["Email:FromAddress"]));
            message.To.Add(new MailboxAddress(toName, toEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
            if (attachment is not null && attachmentName is not null)
                bodyBuilder.Attachments.Add(attachmentName, attachment, new ContentType("application", "pdf"));

            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(config["Email:Host"], int.Parse(config["Email:Port"]!), MailKit.Security.SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(config["Email:Username"], config["Email:Password"]);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email to {Email}", toEmail);
        }
    }
}
