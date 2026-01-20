using System.Collections.Generic;

namespace XmlInvoiceTransformer
{
    /// <summary>
    /// Root configuration class that maps to appsettings.json
    /// </summary>
    public class AppSettings
    {
        public FolderSettings FolderSettings { get; set; } = new();
        public EmailSettings EmailSettings { get; set; } = new();
    }

    /// <summary>
    /// Folder path configuration
    /// </summary>
    public class FolderSettings
    {
        /// <summary>
        /// Folder where input XML files are dropped for processing
        /// </summary>
        public string InputFolder { get; set; } = @"C:\InvoiceProcessor\Input";

        /// <summary>
        /// Folder where transformed XML files are saved
        /// </summary>
        public string OutputFolder { get; set; } = @"C:\InvoiceProcessor\Output";

        /// <summary>
        /// Folder where successfully processed original files are moved
        /// </summary>
        public string ArchiveFolder { get; set; } = @"C:\InvoiceProcessor\Output\Archive";

        /// <summary>
        /// Folder where failed files are moved for manual review
        /// </summary>
        public string ErrorFolder { get; set; } = @"C:\InvoiceProcessor\Output\Errors";

        /// <summary>
        /// Folder where daily log files are stored
        /// </summary>
        public string LogFolder { get; set; } = @"C:\InvoiceProcessor\Output\Logs";

        /// <summary>
        /// If true, original files are moved to Archive folder after processing. 
        /// If false, original files are deleted after successful processing.
        /// </summary>
        public bool ArchiveProcessedFiles { get; set; } = true;

        /// <summary>
        /// How often (in seconds) to check for new files (backup for FileSystemWatcher)
        /// </summary>
        public int PollingIntervalSeconds { get; set; } = 5;
    }

    /// <summary>
    /// Email notification configuration
    /// </summary>
    public class EmailSettings
    {
        /// <summary>
        /// Set to true to enable email notifications for errors
        /// </summary>
        public bool EnableEmailNotifications { get; set; } = false;

        /// <summary>
        /// SMTP server address (e.g., smtp.office365.com, smtp.gmail.com)
        /// </summary>
        public string SmtpServer { get; set; } = "";

        /// <summary>
        /// SMTP server port (typically 587 for TLS, 465 for SSL, 25 for unencrypted)
        /// </summary>
        public int SmtpPort { get; set; } = 587;

        /// <summary>
        /// Use SSL/TLS encryption for email
        /// </summary>
        public bool UseSsl { get; set; } = true;

        /// <summary>
        /// Set to true if the SMTP server requires authentication
        /// </summary>
        public bool RequiresAuthentication { get; set; } = true;

        /// <summary>
        /// Email address that sends the notifications
        /// </summary>
        public string SenderEmail { get; set; } = "";

        /// <summary>
        /// Display name for the sender
        /// </summary>
        public string SenderName { get; set; } = "Invoice Processor";

        /// <summary>
        /// Username for SMTP authentication (often the same as SenderEmail)
        /// </summary>
        public string Username { get; set; } = "";

        /// <summary>
        /// Password for SMTP authentication
        /// </summary>
        public string Password { get; set; } = "";

        /// <summary>
        /// List of people to receive error notifications
        /// </summary>
        public List<EmailRecipient> Recipients { get; set; } = new();

        /// <summary>
        /// Send a daily summary email at the specified time
        /// </summary>
        public bool SendDailySummary { get; set; } = true;

        /// <summary>
        /// Time to send daily summary (24-hour format, e.g., "17:00")
        /// </summary>
        public string DailySummaryTime { get; set; } = "17:00";
    }

    /// <summary>
    /// Represents an email recipient
    /// </summary>
    public class EmailRecipient
    {
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
    }
}