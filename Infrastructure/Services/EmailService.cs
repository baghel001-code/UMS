using Infrastructure.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services
{
    public class EmailService
    {
        private readonly EmailOptions _opts;


        public EmailService(EmailOptions opts) => _opts = opts;


        public Task SendAsync(string to, string subject, string body)
        {
            using var client = new SmtpClient(_opts.Host, _opts.Port)
            {
                EnableSsl = _opts.UseSsl,
            };


            if (!string.IsNullOrEmpty(_opts.Username))
            {
                client.Credentials = new NetworkCredential(_opts.Username, _opts.Password);
            }


            var msg = new MailMessage(_opts.From, to, subject, body) { IsBodyHtml = true };
            return client.SendMailAsync(msg);
        }
    }
}
