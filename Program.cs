using Azure.AI.FormRecognizer.DocumentAnalysis;
using AzureDoc.IDP.Engine.Configurations;
using AzureDoc.IDP.Engine.Helpers;
using AzureDoc.IDP.Engine.Models;
using AzureDoc.IDP.Engine.Services;
using System.Text;

namespace GoogleAIStudioProject
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine(">>>>Doc Intelligence Service START<<<<");
            //string filePath = @"C:\Users\01101006\Downloads\TEST\ALL.pdf";
            string filePath = @"H:\PDFs\M106.pdf";
            string rootTempPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            string outputFolderName = $"Operation_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}";
            string targetFolder = Path.Combine(rootTempPath, outputFolderName);


            AzureSettings azureSettings = ConfigLoader.LoadSettings();
            IDocumentIntelligenceService docIntelligenceService = new DocumentIntelligenceService(azureSettings.Endpoint, azureSettings.ApiKey);
            var ProcessingSummary = await docIntelligenceService.AnalyzeInParallelAsync(filePath);

            var fileNameOnly = ProcessingSummary.FileName;
            ExportPageLogs(ProcessingSummary);
            ExportOperationLogs(ProcessingSummary);

            var Data = ProcessingSummary.Results
                .Where(item => item.Value != null)
                .SelectMany(item => ValveTableParser.Parse(item.Value, fileNameOnly, item.Key))
                .ToList();

            Console.WriteLine($"page\tNPD\tTag No.\tItem\tL\tMinConfidence");
            foreach (var item in Data)
            {
                Console.WriteLine($"{item.PageIndex}\t{item.Size}\t{item.TagNo}\t{item.Item}\t{item.L}\t{item.MinConfidence}");
            }

            Console.ReadKey();
        }
        private static void ExportOperationLogs(DocumentResponse response)
        {
            try
            {
                if (!Directory.Exists(response.TargetFolder)) Directory.CreateDirectory(response.TargetFolder);

                string baseFileName = Path.GetFileNameWithoutExtension(response.FileName);

                // --- 檔案 1：[檔名]-Summary.txt ---
                string summaryPath = Path.Combine(response.TargetFolder, $"{baseFileName}-Summary.txt");
                using (var writer = new StreamWriter(summaryPath, false, Encoding.UTF8))
                {
                    writer.WriteLine("==========================================================");
                    writer.WriteLine("                     Processing Summary                   ");
                    writer.WriteLine("==========================================================");
                    writer.WriteLine($"Operation ID    : {response.OperationId}");
                    writer.WriteLine($"File Name       : {response.FileName}");
                    writer.WriteLine($"Total Pages     : {response.TotalPages}");
                    writer.WriteLine($"Start Time      : {response.StartTime:yyyy-MM-dd HH:mm:ss}");
                    writer.WriteLine($"End Time        : {response.EndTime:yyyy-MM-dd HH:mm:ss}");
                    writer.WriteLine($"Elapsed Time    : {response.TotalDuration.TotalSeconds:F2} s");
                    writer.WriteLine($"Avg per Page    : {response.AveragePageTimeMs:F0} ms");
                    writer.WriteLine($"Success         : {response.IsSuccess}");
                    writer.WriteLine("==========================================================");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[警告] 無法生成日誌檔案: {ex.Message}");
            }
        }
        private static void ExportPageLogs(DocumentResponse response)
        {
            string baseFileName = Path.GetFileNameWithoutExtension(response.FileName);
            string targetFolder = response.TargetFolder;

            foreach (var analyzeResult in response.Results)
            {
                if (analyzeResult.Value == null) continue;
                var page = analyzeResult.Value.Pages[0];
                // --- 檔案 2：[檔名]-p[page]-Lines.txt ---
                string linesPath = Path.Combine(targetFolder, $"{baseFileName}-p{analyzeResult.Key}-Lines.txt");
                File.WriteAllLines(linesPath, page.Lines.Select(l => l.Content), Encoding.UTF8);

                // --- 檔案 3：[檔名]-p[page]-Words.txt ---
                string wordsPath = Path.Combine(targetFolder, $"{baseFileName}-p{analyzeResult.Key}-Words.txt");
                using (var writer = new StreamWriter(wordsPath, false, Encoding.UTF8))
                {
                    writer.WriteLine("Index\tConfidence\tContent\tOffset");
                    for (int i = 0; i < page.Words.Count; i++)
                    {
                        var w = page.Words[i];
                        writer.WriteLine($"{i}\t{w.Confidence:F4}\t{w.Content}\t{w.Span.Index}");
                    }
                }
            }
        }
    }
}
