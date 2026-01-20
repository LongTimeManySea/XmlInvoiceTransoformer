using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace XmlInvoiceTransformer
{
    class Program
    {
        private static AppSettings _settings = new();
        private static ILogger<Program>? _logger;
        private static InvoiceProcessor? _processor;
        private static EmailService? _emailService;
        private static readonly CancellationTokenSource _cancellationTokenSource = new();

        static async Task Main(string[] args)
        {
            // Load configuration
            LoadConfiguration();

            // Setup logging
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddConsole(options =>
                    {
                        options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
                    })
                    .AddProvider(new FileLoggerProvider(_settings.FolderSettings.LogFolder))
                    .SetMinimumLevel(LogLevel.Information);
            });

            _logger = loggerFactory.CreateLogger<Program>();
            var processorLogger = loggerFactory.CreateLogger<InvoiceProcessor>();
            var emailLogger = loggerFactory.CreateLogger<EmailService>();

            _logger.LogInformation("===========================================");
            _logger.LogInformation("   XML Invoice Transformer Service");
            _logger.LogInformation("===========================================");

            // Ensure all directories exist
            EnsureDirectoriesExist();

            // Create email service
            _emailService = new EmailService(_settings.EmailSettings, emailLogger);

            // Create the processor
            _processor = new InvoiceProcessor(
                processorLogger,
                _settings.FolderSettings.OutputFolder,
                _settings.FolderSettings.ArchiveFolder,
                _settings.FolderSettings.ErrorFolder,
                _settings.FolderSettings.ArchiveProcessedFiles,
                _emailService
            );

            LogConfiguration();

            // Handle command line arguments
            if (args.Length > 0 && args[0].ToLower() == "--test-email")
            {
                await TestEmailConfiguration();
                return;
            }

            // Handle graceful shutdown
            Console.CancelKeyPress += async (sender, e) =>
            {
                e.Cancel = true;
                _logger.LogInformation("Shutdown requested...");

                // Send daily summary before shutting down if there were any files processed
                if (_emailService.TodaysSuccessCount > 0 || _emailService.TodaysErrorCount > 0)
                {
                    _logger.LogInformation("Sending final summary email...");
                    await _emailService.SendDailySummaryAsync();
                }

                _cancellationTokenSource.Cancel();
            };

            // Process any existing files in the input folder
            await ProcessExistingFilesAsync();

            // Start watching for new files (this also handles daily summary)
            await WatchForFilesAsync(_cancellationTokenSource.Token);

            _logger.LogInformation("Service stopped.");
        }

        private static void LoadConfiguration()
        {
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings. json");

            if (File.Exists(configPath))
            {
                try
                {
                    var json = File.ReadAllText(configPath);
                    _settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new AppSettings();

                    Console.WriteLine($"Configuration loaded from: {configPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to load configuration:  {ex.Message}");
                    Console.WriteLine("Using default settings.");
                    _settings = new AppSettings();
                }
            }
            else
            {
                Console.WriteLine($"Configuration file not found at: {configPath}");
                Console.WriteLine("Creating default configuration file.. .");

                _settings = new AppSettings();
                SaveDefaultConfiguration(configPath);
            }
        }

        private static void SaveDefaultConfiguration(string configPath)
        {
            try
            {
                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(configPath, json);
                Console.WriteLine($"Default configuration saved to: {configPath}");
                Console.WriteLine("Please edit this file to configure your settings.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not save default configuration: {ex.Message}");
            }
        }

        private static void EnsureDirectoriesExist()
        {
            try
            {
                Directory.CreateDirectory(_settings.FolderSettings.InputFolder);
                Directory.CreateDirectory(_settings.FolderSettings.OutputFolder);
                Directory.CreateDirectory(_settings.FolderSettings.ArchiveFolder);
                Directory.CreateDirectory(_settings.FolderSettings.ErrorFolder);
                Directory.CreateDirectory(_settings.FolderSettings.LogFolder);
                _logger?.LogInformation("All directories verified/created successfully.");
            }
            catch (Exception ex)
            {
                _logger?.LogCritical(ex, "Failed to create required directories.  Exiting.");
                Environment.Exit(1);
            }
        }

        private static void LogConfiguration()
        {
            _logger?.LogInformation("");
            _logger?.LogInformation("Folder Configuration:");
            _logger?.LogInformation("  Input folder:     {InputFolder}", _settings.FolderSettings.InputFolder);
            _logger?.LogInformation("  Output folder:   {OutputFolder}", _settings.FolderSettings.OutputFolder);
            _logger?.LogInformation("  Archive folder:   {ArchiveFolder}", _settings.FolderSettings.ArchiveFolder);
            _logger?.LogInformation("  Error folder:    {ErrorFolder}", _settings.FolderSettings.ErrorFolder);
            _logger?.LogInformation("  Log folder:      {LogFolder}", _settings.FolderSettings.LogFolder);
            _logger?.LogInformation("");
            _logger?.LogInformation("Email Configuration:");
            _logger?.LogInformation("  Email notifications: {Enabled}", _settings.EmailSettings.EnableEmailNotifications ? "Enabled" : "Disabled");

            if (_settings.EmailSettings.EnableEmailNotifications)
            {
                _logger?.LogInformation("  SMTP Server:  {Server}:{Port}", _settings.EmailSettings.SmtpServer, _settings.EmailSettings.SmtpPort);
                _logger?.LogInformation("  Recipients: {Count} configured", _settings.EmailSettings.Recipients.Count);
                _logger?.LogInformation("  Daily summary: {Enabled} at {Time}",
                    _settings.EmailSettings.SendDailySummary ? "Enabled" : "Disabled",
                    _settings.EmailSettings.DailySummaryTime);
            }

            _logger?.LogInformation("");
            _logger?.LogInformation("Watching for XML files...  Press Ctrl+C to stop.");
            _logger?.LogInformation("");
        }

        private static async Task TestEmailConfiguration()
        {
            Console.WriteLine("\nTesting email configuration...\n");

            if (!_settings.EmailSettings.EnableEmailNotifications)
            {
                Console.WriteLine("❌ Email notifications are disabled in configuration.");
                Console.WriteLine("   Set 'EnableEmailNotifications' to true in appsettings.json");
                return;
            }

            if (_settings.EmailSettings.Recipients.Count == 0)
            {
                Console.WriteLine("❌ No email recipients configured.");
                Console.WriteLine("   Add recipients to the 'Recipients' array in appsettings.json");
                return;
            }

            Console.WriteLine($"SMTP Server: {_settings.EmailSettings.SmtpServer}:{_settings.EmailSettings.SmtpPort}");
            Console.WriteLine($"From: {_settings.EmailSettings.SenderEmail}");
            Console.WriteLine($"To: {string.Join(", ", _settings.EmailSettings.Recipients.ConvertAll(r => r.Email))}");
            Console.WriteLine("\nSending test email...");

            // Need to create email service for testing
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var emailLogger = loggerFactory.CreateLogger<EmailService>();
            var testEmailService = new EmailService(_settings.EmailSettings, emailLogger);

            var success = await testEmailService.SendTestEmailAsync();

            if (success)
            {
                Console.WriteLine("\n✅ Test email sent successfully!");
                Console.WriteLine("   Check your inbox to confirm delivery.");
            }
            else
            {
                Console.WriteLine("\n❌ Failed to send test email.");
                Console.WriteLine("   Check your SMTP settings in appsettings.json");
                Console.WriteLine("   Common issues:");
                Console.WriteLine("   - Incorrect SMTP server or port");
                Console.WriteLine("   - Invalid credentials");
                Console.WriteLine("   - Firewall blocking the connection");
                Console.WriteLine("   - Less secure app access disabled (for Gmail)");
            }
        }

        private static async Task ProcessExistingFilesAsync()
        {
            var existingFiles = Directory.GetFiles(_settings.FolderSettings.InputFolder, "*.xml");
            if (existingFiles.Length > 0)
            {
                _logger?.LogInformation("Found {Count} existing file(s) to process.", existingFiles.Length);
                foreach (var file in existingFiles)
                {
                    await ProcessFileWithRetryAsync(file);
                }
            }
        }

        private static async Task WatchForFilesAsync(CancellationToken cancellationToken)
        {
            using var watcher = new FileSystemWatcher(_settings.FolderSettings.InputFolder)
            {
                Filter = "*.xml",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };

            watcher.Created += async (sender, e) =>
            {
                _logger?.LogInformation("New file detected: {FileName}", e.Name);
                await Task.Delay(500);
                await ProcessFileWithRetryAsync(e.FullPath);
            };

            watcher.Renamed += async (sender, e) =>
            {
                if (e.FullPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogInformation("File renamed to XML: {FileName}", e.Name);
                    await Task.Delay(500);
                    await ProcessFileWithRetryAsync(e.FullPath);
                }
            };

            watcher.Error += (sender, e) =>
            {
                _logger?.LogError(e.GetException(), "FileSystemWatcher error occurred.");
            };

            // Track when to send daily summary
            DateTime? lastSummaryDate = null;

            // Main loop with polling and daily summary check
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_settings.FolderSettings.PollingIntervalSeconds * 1000, cancellationToken);

                    // Check for any files that might have been missed
                    var files = Directory.GetFiles(_settings.FolderSettings.InputFolder, "*.xml");
                    foreach (var file in files)
                    {
                        await ProcessFileWithRetryAsync(file);
                    }

                    // Check if it's time to send daily summary
                    await CheckAndSendDailySummaryAsync(lastSummaryDate);
                    if (DateTime.Now.Date != lastSummaryDate?.Date)
                    {
                        if (TimeSpan.TryParse(_settings.EmailSettings.DailySummaryTime, out var summaryTime))
                        {
                            if (DateTime.Now.TimeOfDay >= summaryTime)
                            {
                                lastSummaryDate = DateTime.Now.Date;
                            }
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error during polling cycle.");
                }
            }
        }

        private static async Task CheckAndSendDailySummaryAsync(DateTime? lastSummaryDate)
        {
            if (!_settings.EmailSettings.SendDailySummary || _emailService == null)
                return;

            if (!TimeSpan.TryParse(_settings.EmailSettings.DailySummaryTime, out var summaryTime))
                return;

            var now = DateTime.Now;

            // Check if we should send summary (haven't sent today and it's past the scheduled time)
            if (lastSummaryDate?.Date != now.Date && now.TimeOfDay >= summaryTime)
            {
                // Only send if there was any activity
                if (_emailService.TodaysSuccessCount > 0 || _emailService.TodaysErrorCount > 0)
                {
                    _logger?.LogInformation("Sending scheduled daily summary email...");
                    await _emailService.SendDailySummaryAsync();
                }
            }
        }

        private static async Task ProcessFileWithRetryAsync(string filePath, int maxRetries = 3)
        {
            if (!File.Exists(filePath)) return;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    if (IsFileLocked(filePath))
                    {
                        _logger?.LogDebug("File is locked, waiting...  (Attempt {Attempt}/{MaxRetries})", attempt, maxRetries);
                        await Task.Delay(1000 * attempt);
                        continue;
                    }

                    _processor?.ProcessFile(filePath);
                    return;
                }
                catch (IOException) when (attempt < maxRetries)
                {
                    _logger?.LogWarning("File access error, retrying... (Attempt {Attempt}/{MaxRetries})", attempt, maxRetries);
                    await Task.Delay(1000 * attempt);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to process file after {Attempts} attempts:  {FilePath}", attempt, filePath);
                    break;
                }
            }
        }

        private static bool IsFileLocked(string filePath)
        {
            try
            {
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                return false;
            }
            catch (IOException)
            {
                return true;
            }
        }
    }
}