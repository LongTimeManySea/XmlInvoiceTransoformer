using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace XmlInvoiceTransformer
{
    /// <summary>
    /// Handles sending email notifications for errors and daily summaries
    /// </summary>
    public class EmailService
    {
        private readonly EmailSettings _settings;
        private readonly ILogger<EmailService> _logger;
        private readonly List<ProcessingError> _todaysErrors = new();
        private readonly object _errorLock = new();

        public int TodaysSuccessCount { get; set; }
        public int TodaysErrorCount => _todaysErrors.Count;

        public EmailService(EmailSettings settings, ILogger<EmailService> logger)
        {
            _settings = settings;
            _logger = logger;
        }

        /// <summary>
        /// Records an error for the daily summary
        /// </summary>
        public void RecordError(string fileName, string errorMessage, Exception? exception = null)
        {
            lock (_errorLock)
            {
                _todaysErrors.Add(new ProcessingError
                {
                    Timestamp = DateTime.Now,
                    FileName = fileName,
                    ErrorMessage = errorMessage,
                    ExceptionDetails = exception?.ToString()
                });
            }
        }

        /// <summary>
        /// Sends an immediate error notification email
        /// </summary>
        public async Task SendErrorNotificationAsync(string fileName, string errorMessage, Exception? exception = null)
        {
            if (!_settings.EnableEmailNotifications || _settings.Recipients.Count == 0)
            {
                return;
            }

            try
            {
                var subject = $"⚠️ Invoice Processing Error: {fileName}";
                var body = BuildErrorEmailBody(fileName, errorMessage, exception);

                await SendEmailAsync(subject, body);
                _logger.LogInformation("Error notification email sent for:  {FileName}", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send error notification email for: {FileName}", fileName);
            }
        }

        /// <summary>
        /// Sends the daily summary email
        /// </summary>
        public async Task SendDailySummaryAsync()
        {
            if (!_settings.EnableEmailNotifications || !_settings.SendDailySummary || _settings.Recipients.Count == 0)
            {
                return;
            }

            try
            {
                var subject = $"📊 Invoice Processor Daily Summary - {DateTime.Now:dd/MM/yyyy}";
                var body = BuildDailySummaryBody();

                await SendEmailAsync(subject, body);
                _logger.LogInformation("Daily summary email sent successfully.");

                // Clear today's errors after sending summary
                lock (_errorLock)
                {
                    _todaysErrors.Clear();
                }
                TodaysSuccessCount = 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send daily summary email.");
            }
        }

        private string BuildErrorEmailBody(string fileName, string errorMessage, Exception? exception)
        {
            var sb = new StringBuilder();

            sb.AppendLine("<html><body style='font-family: Arial, sans-serif;'>");
            sb.AppendLine("<div style='background-color: #f8d7da; border:  1px solid #f5c6cb; border-radius: 5px; padding: 15px; margin-bottom: 20px;'>");
            sb.AppendLine("<h2 style='color: #721c24; margin-top: 0;'>⚠️ Invoice Processing Error</h2>");
            sb.AppendLine("</div>");

            sb.AppendLine("<table style='border-collapse: collapse; width: 100%; margin-bottom: 20px;'>");
            sb.AppendLine($"<tr><td style='padding: 8px; border: 1px solid #ddd; font-weight: bold; width: 150px;'>File Name: </td><td style='padding: 8px; border: 1px solid #ddd;'>{fileName}</td></tr>");
            sb.AppendLine($"<tr><td style='padding: 8px; border: 1px solid #ddd; font-weight: bold;'>Time:</td><td style='padding:  8px; border: 1px solid #ddd;'>{DateTime.Now:dd/MM/yyyy HH:mm:ss}</td></tr>");
            sb.AppendLine($"<tr><td style='padding: 8px; border: 1px solid #ddd; font-weight: bold;'>Error: </td><td style='padding: 8px; border: 1px solid #ddd; color: #dc3545;'>{errorMessage}</td></tr>");
            sb.AppendLine("</table>");

            if (exception != null)
            {
                sb.AppendLine("<h3>Technical Details:</h3>");
                sb.AppendLine($"<pre style='background-color: #f4f4f4; padding: 10px; border-radius: 5px; overflow-x: auto; font-size: 12px;'>{exception}</pre>");
            }

            sb.AppendLine("<p style='color: #666; font-size: 12px;'>The file has been moved to the Errors folder for manual review.</p>");
            sb.AppendLine("<hr style='border: none; border-top: 1px solid #ddd; margin:  20px 0;'>");
            sb.AppendLine("<p style='color: #999; font-size: 11px;'>This is an automated message from the Invoice Processor service.</p>");
            sb.AppendLine("</body></html>");

            return sb.ToString();
        }

        private string BuildDailySummaryBody()
        {
            var sb = new StringBuilder();
            var hasErrors = _todaysErrors.Count > 0;

            sb.AppendLine("<html><body style='font-family: Arial, sans-serif;'>");

            // Header
            var headerColor = hasErrors ? "#fff3cd" : "#d4edda";
            var headerBorder = hasErrors ? "#ffc107" : "#28a745";
            var headerText = hasErrors ? "#856404" : "#155724";
            var emoji = hasErrors ? "📊" : "✅";

            sb.AppendLine($"<div style='background-color:  {headerColor}; border-left: 4px solid {headerBorder}; padding: 15px; margin-bottom: 20px;'>");
            sb.AppendLine($"<h2 style='color: {headerText}; margin:  0;'>{emoji} Daily Processing Summary</h2>");
            sb.AppendLine($"<p style='color: {headerText}; margin: 5px 0 0 0;'>{DateTime.Now:dddd, dd MMMM yyyy}</p>");
            sb.AppendLine("</div>");

            // Summary Statistics
            sb.AppendLine("<h3>📈 Statistics</h3>");
            sb.AppendLine("<table style='border-collapse: collapse; width: 300px; margin-bottom: 20px;'>");
            sb.AppendLine($"<tr><td style='padding: 10px; border: 1px solid #ddd; font-weight: bold;'>✅ Successful</td><td style='padding: 10px; border: 1px solid #ddd; text-align: center; color: #28a745; font-weight: bold;'>{TodaysSuccessCount}</td></tr>");
            sb.AppendLine($"<tr><td style='padding: 10px; border: 1px solid #ddd; font-weight: bold;'>❌ Failed</td><td style='padding:  10px; border: 1px solid #ddd; text-align: center; color: #dc3545; font-weight: bold;'>{_todaysErrors.Count}</td></tr>");
            sb.AppendLine($"<tr><td style='padding: 10px; border: 1px solid #ddd; font-weight: bold;'>📁 Total Processed</td><td style='padding:  10px; border: 1px solid #ddd; text-align: center; font-weight: bold;'>{TodaysSuccessCount + _todaysErrors.Count}</td></tr>");
            sb.AppendLine("</table>");

            // Error Details
            if (hasErrors)
            {
                sb.AppendLine("<h3>❌ Error Details</h3>");
                sb.AppendLine("<table style='border-collapse: collapse; width: 100%; margin-bottom: 20px;'>");
                sb.AppendLine("<tr style='background-color: #f8f9fa;'>");
                sb.AppendLine("<th style='padding: 10px; border: 1px solid #ddd; text-align: left;'>Time</th>");
                sb.AppendLine("<th style='padding:  10px; border: 1px solid #ddd; text-align: left;'>File Name</th>");
                sb.AppendLine("<th style='padding: 10px; border: 1px solid #ddd; text-align: left;'>Error</th>");
                sb.AppendLine("</tr>");

                lock (_errorLock)
                {
                    foreach (var error in _todaysErrors)
                    {
                        sb.AppendLine("<tr>");
                        sb.AppendLine($"<td style='padding:  8px; border: 1px solid #ddd;'>{error.Timestamp:HH:mm:ss}</td>");
                        sb.AppendLine($"<td style='padding:  8px; border: 1px solid #ddd;'>{error.FileName}</td>");
                        sb.AppendLine($"<td style='padding: 8px; border: 1px solid #ddd; color: #dc3545;'>{error.ErrorMessage}</td>");
                        sb.AppendLine("</tr>");
                    }
                }

                sb.AppendLine("</table>");
                sb.AppendLine("<p style='color: #666;'>📂 Failed files have been moved to the Errors folder for manual review.</p>");
            }
            else
            {
                sb.AppendLine("<div style='background-color: #d4edda; border-radius: 5px; padding: 15px; text-align: center;'>");
                sb.AppendLine("<p style='color: #155724; margin: 0; font-size: 16px;'>🎉 All files processed successfully today!</p>");
                sb.AppendLine("</div>");
            }

            sb.AppendLine("<hr style='border: none; border-top: 1px solid #ddd; margin: 20px 0;'>");
            sb.AppendLine("<p style='color: #999; font-size: 11px;'>This is an automated daily summary from the Invoice Processor service. </p>");
            sb.AppendLine("</body></html>");

            return sb.ToString();
        }

        private async Task SendEmailAsync(string subject, string htmlBody)
        {
            using var client = new SmtpClient(_settings.SmtpServer, _settings.SmtpPort)
            {
                EnableSsl = _settings.UseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };

            if (_settings.RequiresAuthentication)
            {
                client.Credentials = new NetworkCredential(_settings.Username, _settings.Password);
            }

            using var message = new MailMessage
            {
                From = new MailAddress(_settings.SenderEmail, _settings.SenderName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };

            foreach (var recipient in _settings.Recipients)
            {
                if (!string.IsNullOrWhiteSpace(recipient.Email))
                {
                    message.To.Add(new MailAddress(recipient.Email, recipient.Name));
                }
            }

            await client.SendMailAsync(message);
        }

        /// <summary>
        /// Test the email configuration by sending a test email
        /// </summary>
        public async Task<bool> SendTestEmailAsync()
        {
            try
            {
                var subject = "🧪 Invoice Processor - Test Email";
                var body = @"
                    <html><body style='font-family: Arial, sans-serif;'>
                    <div style='background-color:  #cce5ff; border: 1px solid #004085; border-radius: 5px; padding: 15px;'>
                    <h2 style='color: #004085; margin-top: 0;'>✅ Test Email Successful! </h2>
                    <p>This is a test email from the Invoice Processor service.</p>
                    <p>If you received this email, your email configuration is working correctly.</p>
                    </div>
                    <p style='color: #999; font-size: 11px; margin-top: 20px;'>Sent at: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm: ss") + @"</p>
                    </body></html>";

                await SendEmailAsync(subject, body);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Test email failed.");
                return false;
            }
        }
    }

    public class ProcessingError
    {
        public DateTime Timestamp { get; set; }
        public string FileName { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public string? ExceptionDetails { get; set; }
    }
}