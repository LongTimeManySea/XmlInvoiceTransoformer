using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Xml.Linq;

namespace XmlInvoiceTransformer
{
    /// <summary>
    /// Handles the processing of individual invoice files
    /// </summary>
    public class InvoiceProcessor
    {
        private readonly ILogger<InvoiceProcessor> _logger;
        private readonly string _outputFolder;
        private readonly string _archiveFolder;
        private readonly string _errorFolder;
        private readonly bool _archiveProcessedFiles;
        private readonly EmailService? _emailService;
        private readonly InvoiceTransformer _transformer;

        private int _successCount;
        private int _errorCount;

        public InvoiceProcessor(
            ILogger<InvoiceProcessor> logger,
            string outputFolder,
            string archiveFolder,
            string errorFolder,
            bool archiveProcessedFiles,
            EmailService? emailService = null)
        {
            _logger = logger;
            _outputFolder = outputFolder;
            _archiveFolder = archiveFolder;
            _errorFolder = errorFolder;
            _archiveProcessedFiles = archiveProcessedFiles;
            _emailService = emailService;
            _transformer = new InvoiceTransformer();
        }

        public void ProcessFile(string inputFilePath)
        {
            var fileName = Path.GetFileName(inputFilePath);
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(inputFilePath);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            _logger.LogInformation("Processing:  {FileName}", fileName);

            try
            {
                // Load and validate input XML
                XDocument inputDoc;
                try
                {
                    inputDoc = XDocument.Load(inputFilePath);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to parse XML:  {ex.Message}", ex);
                }

                // Validate it's the expected format
                if (inputDoc.Root?.Name.LocalName != "SalesInvoicePrint")
                {
                    throw new InvalidOperationException(
                        $"Unexpected root element '{inputDoc.Root?.Name.LocalName}'.  Expected 'SalesInvoicePrint'.");
                }

                // Transform the document
                XDocument outputDoc = _transformer.Transform(inputDoc);

                // Generate output filename with timestamp to avoid overwrites
                var outputFileName = $"{fileNameWithoutExt}_Transformed_{timestamp}.xml";
                var outputFilePath = Path.Combine(_outputFolder, outputFileName);

                // Save the transformed document
                outputDoc.Save(outputFilePath);

                _logger.LogInformation("✓ Successfully transformed:  {FileName} -> {OutputFileName}", fileName, outputFileName);

                // Handle the original file (archive or delete)
                HandleProcessedFile(inputFilePath, fileName, timestamp);

                _successCount++;
                if (_emailService != null)
                {
                    _emailService.TodaysSuccessCount++;
                }

                _logger.LogInformation("Running totals - Success: {Success}, Errors: {Errors}", _successCount, _errorCount);
            }
            catch (Exception ex)
            {
                _errorCount++;
                _logger.LogError(ex, "✗ Failed to process:  {FileName}", fileName);

                // Record error and send notification
                _emailService?.RecordError(fileName, ex.Message, ex);
                _ = _emailService?.SendErrorNotificationAsync(fileName, ex.Message, ex);

                // Move to error folder for manual review
                MoveToErrorFolder(inputFilePath, fileName, timestamp, ex.Message);

                _logger.LogInformation("Running totals - Success: {Success}, Errors: {Errors}", _successCount, _errorCount);
            }
        }

        private void HandleProcessedFile(string filePath, string fileName, string timestamp)
        {
            try
            {
                if (_archiveProcessedFiles)
                {
                    var archiveFileName = $"{Path.GetFileNameWithoutExtension(fileName)}_{timestamp}{Path.GetExtension(fileName)}";
                    var archivePath = Path.Combine(_archiveFolder, archiveFileName);
                    File.Move(filePath, archivePath);
                    _logger.LogDebug("Archived original file to: {ArchivePath}", archivePath);
                }
                else
                {
                    File.Delete(filePath);
                    _logger.LogDebug("Deleted original file: {FileName}", fileName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not archive/delete original file: {FileName}", fileName);
            }
        }

        private void MoveToErrorFolder(string filePath, string fileName, string timestamp, string errorMessage)
        {
            try
            {
                var errorFileName = $"{Path.GetFileNameWithoutExtension(fileName)}_{timestamp}_ERROR{Path.GetExtension(fileName)}";
                var errorFilePath = Path.Combine(_errorFolder, errorFileName);

                // Move the problematic file
                File.Move(filePath, errorFilePath);

                // Create an error details file alongside it
                var errorDetailsPath = Path.Combine(_errorFolder, $"{Path.GetFileNameWithoutExtension(errorFileName)}.txt");
                File.WriteAllText(errorDetailsPath,
                    $"Error processing file: {fileName}\n" +
                    $"Timestamp: {DateTime.Now:yyyy-MM-dd HH: mm:ss}\n" +
                    $"Error: {errorMessage}\n");

                _logger.LogInformation("Moved failed file to error folder: {ErrorFileName}", errorFileName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not move file to error folder: {FileName}", fileName);
            }
        }
    }
}