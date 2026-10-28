using System.Net.Mail;
using System.Net;
using SystemPanel.Services;

namespace SystemPanel.Services
{
    public class MailService(IConfiguration config) : IMailService
    {
        private readonly IConfiguration _config = config;

        public async Task SendAlertAsync(string subject, string message)
        {
            var from = _config["Smtp:User"] ?? throw new InvalidOperationException("SMTP kullanıcı tanımlı değil.");
            var to = _config["Smtp:To"] ?? throw new InvalidOperationException("SMTP alıcı tanımlı değil.");
            var host = _config["Smtp:Host"] ?? throw new InvalidOperationException("SMTP host tanımlı değil.");
            var pass = _config["Smtp:Pass"] ?? throw new InvalidOperationException("SMTP şifre tanımlı değil.");
            var port = int.TryParse(_config["Smtp:Port"], out var p) ? p : throw new InvalidOperationException("SMTP port geçersiz.");

            var email = new MailMessage
            {
                From = new MailAddress(from),
                Subject = subject,
                Body = message
            };

            email.To.Add(to);

            using var smtp = new SmtpClient(host, port)
            {
                Credentials = new NetworkCredential(from, pass),
                EnableSsl = true
            };

            await smtp.SendMailAsync(email);
        }
    }
}
