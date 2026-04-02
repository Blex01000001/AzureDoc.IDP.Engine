using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using AzureDoc.IDP.Engine.Configurations;
using AzureDoc.IDP.Engine.Helpers;
using AzureDoc.IDP.Engine.Models;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading.RateLimiting;


namespace AzureDoc.IDP.Engine.Services
{
    public class DocumentIntelligenceService : IDocumentIntelligenceService
    {
        private string _targetFolder;
        private readonly AzureSettings _settings;
        private readonly ValveTableParser _parser;
        private readonly DocumentAnalysisClient _client;
        private ProcessingSummary _summary = new ProcessingSummary();
        private readonly string outputFolderName = $"Operation_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}";
        private readonly TokenBucketRateLimiter _freeLimiter = new(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 4, // 桶子容量，允許極小規模的突發
            ReplenishmentPeriod = TimeSpan.FromSeconds(4), // 每 4 秒補一次
            TokensPerPeriod = 1,
            AutoReplenishment = true,
            QueueLimit = 100
        });
        private readonly TokenBucketRateLimiter _payLimiter = new(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 1,
            ReplenishmentPeriod = TimeSpan.FromMilliseconds(80),
            TokensPerPeriod = 1,
            AutoReplenishment = true,
            QueueLimit = 100
        });
        public DocumentIntelligenceService(AzureSettings settings)
        {
            _settings = settings;
            _client = new DocumentAnalysisClient(new Uri(settings.Endpoint), new AzureKeyCredential(settings.ApiKey));
            _parser = new ValveTableParser();
            _summary.StartTime = DateTime.Now;
            string rootTempPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            _targetFolder = Path.Combine(rootTempPath, outputFolderName);
            if (!Directory.Exists(_targetFolder)) Directory.CreateDirectory(_targetFolder);
        }

        /// <summary>
        /// 高效能並行提取數據 (適用於 S0 付費版)
        /// </summary>
        /// <param name="filePath">PDF完整路徑</param>
        /// <param name="maxDegreeOfParallelism">最大並行數</param>
        /// <returns></returns>
        public async Task<ProcessingSummary> AnalyzeInParallelAsync(string filePath, int maxDegreeOfParallelism = 10)
        {
            _summary.FileName = Path.GetFileName(filePath);
            _summary.FilePath = filePath;
            var sw = Stopwatch.StartNew();
            var allResults = new ConcurrentBag<ValveDimensionData>();
            var analyzeResults = new ConcurrentBag<AnalyzeResult>();
            string fileNameOnly = Path.GetFileNameWithoutExtension(filePath);

            using var inputDocument = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
            int totalPages = inputDocument.PageCount;
            _summary.TotalPages = totalPages;

            using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);// 控制併發任務數
            var tasks = new List<Task>();

            for (int i = 0; i < totalPages; i++)
            {
                int pageNumber = i + 1;
                await semaphore.WaitAsync();// 步驟 A: 先過 Semaphore (確保執行緒/連線數受控)
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        // 步驟 B: 再過 TokenBucket (確保每秒請求數受控)
                        using var lease = await _payLimiter.AcquireAsync(1);
                        if (!lease.IsAcquired) return;

                        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [發送] 頁面 {pageNumber}");

                        // 1. 準備單頁 Stream
                        using var ms = PrepareSinglePageStream(filePath, pageNumber - 1);

                        // 2. 呼叫 Azure API
                        var operation = await _client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-layout", ms);
                        analyzeResults.Add(operation.Value);
                        // 3. 解析
                        var pageData = _parser.Parse(operation.Value, fileNameOnly, pageNumber);
                        foreach (var item in pageData) allResults.Add(item);

                        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [完成] 頁面 {pageNumber}");
                        ExportPageLogs(fileNameOnly, pageNumber, operation.Value.Pages[0]);
                    }
                    catch (Exception ex)
                    {
                        _summary.IsSuccess = false;
                        _summary.ErrorMessage = ex.Message;
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }
            await Task.WhenAll(tasks);
            sw.Stop();
            _summary.EndTime = DateTime.Now;
            _summary.Data = allResults.OrderBy(x => x.PageIndex).ToList();
            ExportOperationLogs(analyzeResults.ToList());
            return _summary;
        }
        public async Task<ProcessingSummary> AnalyzeInParallelAsync_OLD(string filePath, int maxDegreeOfParallelism = 10)
        {
            var summary = new ProcessingSummary
            {
                FileName = Path.GetFileName(filePath),
                FilePath = filePath,
                StartTime = DateTime.Now
            };
            var sw = Stopwatch.StartNew();
            var allResults = new ConcurrentBag<ValveDimensionData>();
            string fileNameOnly = Path.GetFileNameWithoutExtension(filePath);

            using var inputDocument = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
            int totalPages = inputDocument.PageCount;
            summary.TotalPages = inputDocument.PageCount;

            // 使用 SemaphoreSlim 控制並行數，避免觸發 Azure 429 錯誤 (TPS 限制)
            using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);
            var tasks = new List<Task>();

            for (int i = 0; i < totalPages; i++)
            {
                int pageNumber = i + 1;
                await semaphore.WaitAsync(); // 取得許可

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        // 1. 準備單頁 Stream (需在每個 Task 內部獨立處理以確保安全)
                        using var ms = PrepareSinglePageStream(filePath, pageNumber - 1);

                        // 2. 呼叫 Azure API
                        var operation = await _client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-layout", ms);

                        // 3. 解析並加入結果集
                        var pageData = _parser.Parse(operation.Value, fileNameOnly, pageNumber);
                        foreach (var item in pageData) allResults.Add(item);

                        Console.WriteLine($"[完成] 頁面 {pageNumber} 處理完畢。");
                    }
                    catch (Exception ex)
                    {
                        summary.IsSuccess = false;
                        summary.ErrorMessage = ex.Message;
                    }
                    finally
                    {
                        semaphore.Release(); // 釋放許可
                    }
                }));
            }

            await Task.WhenAll(tasks);
            sw.Stop();
            summary.EndTime = DateTime.Now;
            summary.Data = allResults.OrderBy(x => x.PageIndex).ToList(); // 最後依頁碼排序
            return summary;
        }
        /// <summary>
        /// 低效能循序提取數據 (適用於 F0 免費版)
        /// </summary>
        /// <param name="filePath">PDF完整路徑</param>
        /// <returns></returns>
        public async Task<ProcessingSummary> AnalyzeInSequentialAsync(string filePath)
        {
            _summary.FileName = Path.GetFileName(filePath);
            _summary.FilePath = filePath;
            var sw = Stopwatch.StartNew();
            var allResults = new List<ValveDimensionData>();
            var analyzeResults = new List<AnalyzeResult>();
            string fileNameOnly = Path.GetFileNameWithoutExtension(filePath);

            using var inputDocument = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
            _summary.TotalPages = inputDocument.PageCount;

            for (int i = 0; i < inputDocument.PageCount; i++)
            {
                int pageNumber = i + 1;
                using var lease = await _freeLimiter.AcquireAsync(permitCount: 1);
                if (!lease.IsAcquired)
                {
                    // 通常只有在 QueueLimit 滿了才會走到這
                    Console.WriteLine($"[警告] 無法取得執行許可，略過第 {pageNumber} 頁");
                    continue;
                }
                Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [處理中] {fileNameOnly} - 第 {pageNumber} 頁");
                using var singlePagePdf = new PdfDocument();
                singlePagePdf.AddPage(inputDocument.Pages[i]);
                using var ms = new MemoryStream();
                singlePagePdf.Save(ms, false);
                ms.Position = 0;

                // 呼叫 Azure
                var operation = await _client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-layout", ms);
                analyzeResults.Add(operation.Value);

                // 解析資料
                var pageData = _parser.Parse(operation.Value, fileNameOnly, pageNumber);
                allResults.AddRange(pageData);

                ExportPageLogs(fileNameOnly, pageNumber, operation.Value.Pages[0]);
            }
            sw.Stop();
            _summary.EndTime = DateTime.Now;
            _summary.Data = allResults;
            ExportOperationLogs(analyzeResults);
            return _summary;
        }
        public async Task<ProcessingSummary> AnalyzeInSequentialAsync_OLD(string filePath)
        {
            _summary.FileName = Path.GetFileName(filePath);
            _summary.FilePath = filePath;
            var sw = Stopwatch.StartNew();
            var allResults = new List<ValveDimensionData>();
            var analyzeResults = new List<AnalyzeResult>();
            string fileNameOnly = Path.GetFileNameWithoutExtension(filePath);

            using var inputDocument = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
            _summary.TotalPages = inputDocument.PageCount;
            for (int i = 0; i < inputDocument.PageCount; i++)
            {
                int pageNumber = i + 1;
                Console.WriteLine($"[處理中] {fileNameOnly} - 第 {pageNumber} 頁");

                // 分頁處理
                using var singlePagePdf = new PdfDocument();
                singlePagePdf.AddPage(inputDocument.Pages[i]);
                using var ms = new MemoryStream();
                singlePagePdf.Save(ms, false);
                ms.Position = 0;

                // 呼叫 Azure
                var operation = await _client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-layout", ms);
                analyzeResults.Add(operation.Value);
                // 解析資料
                var pageData = _parser.Parse(operation.Value, fileNameOnly, pageNumber);
                allResults.AddRange(pageData);

                ExportPageLogs(fileNameOnly, i + 1, operation.Value.Pages[0]);
                // 免費版頻率限制處理
                if (i < inputDocument.PageCount - 1)
                {
                    await Task.Delay(4100);
                }
            }
            sw.Stop();
            _summary.EndTime = DateTime.Now;
            _summary.Data = allResults;
            ExportOperationLogs(analyzeResults);
            return _summary;
        }

        /// <summary>
        /// 輔助方法：從原始檔案中切出單頁 Stream
        /// </summary>
        private MemoryStream PrepareSinglePageStream(string filePath, int pageIndex)
        {
            // 注意：PdfSharp 的 PDF 物件不是 Thread-safe，
            // 在並行模式下，建議每次都重新開啟 Open 或是加鎖
            using var input = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
            using var output = new PdfDocument();
            output.AddPage(input.Pages[pageIndex]);
            var ms = new MemoryStream();
            output.Save(ms, false);
            ms.Position = 0;
            return ms;
        }

        /// <summary>
        /// 將本次執行的摘要與 OCR 原始數據存入臨時資料夾
        /// </summary>
        private void ExportOperationLogs(List<AnalyzeResult> results)
        {
            try
            {
                if (!Directory.Exists(_targetFolder)) Directory.CreateDirectory(_targetFolder);

                string baseFileName = Path.GetFileNameWithoutExtension(_summary.FileName);

                // --- 檔案 1：[檔名]-Summary.txt ---
                string summaryPath = Path.Combine(_targetFolder, $"{baseFileName}-Summary.txt");
                using (var writer = new StreamWriter(summaryPath, false, Encoding.UTF8))
                {
                    writer.WriteLine("==========================================================");
                    writer.WriteLine("                     Processing Summary                   ");
                    writer.WriteLine("==========================================================");
                    writer.WriteLine($"Operation ID    : {_summary.OperationId}");
                    writer.WriteLine($"File Name       : {_summary.FileName}");
                    writer.WriteLine($"Total Pages     : {_summary.TotalPages}");
                    writer.WriteLine($"Start Time      : {_summary.StartTime:yyyy-MM-dd HH:mm:ss}");
                    writer.WriteLine($"End Time        : {_summary.EndTime:yyyy-MM-dd HH:mm:ss}");
                    writer.WriteLine($"Elapsed Time    : {_summary.TotalDuration.TotalSeconds:F2} s");
                    writer.WriteLine($"Avg per Page    : {_summary.AveragePageTimeMs:F0} ms");
                    writer.WriteLine($"Success         : {_summary.IsSuccess}");
                    writer.WriteLine("==========================================================");
                    writer.WriteLine("\n=== Extracted Data (CSV Format) ===");
                    writer.WriteLine("page,Size,Item,TagNo,L,Confidence");
                    foreach (var item in _summary.Data)
                    {
                        writer.WriteLine($"{item.PageIndex},{item.Size},{item.Item},{item.TagNo},{item.L},{item.MinConfidence:F3}");
                    }
                }

                Console.WriteLine($"[系統] 執行日誌已生成於: {_targetFolder}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[警告] 無法生成日誌檔案: {ex.Message}");
            }
        }
        private void ExportPageLogs(string FileName, int pNum, DocumentPage page)
        {   
            string baseFileName = Path.GetFileNameWithoutExtension(FileName);

            // --- 檔案 2：[檔名]-p[page]-Lines.txt ---
            string linesPath = Path.Combine(_targetFolder, $"{baseFileName}-p{pNum}-Lines.txt");
            File.WriteAllLines(linesPath, page.Lines.Select(l => l.Content), Encoding.UTF8);

            // --- 檔案 3：[檔名]-p[page]-Words.txt ---
            string wordsPath = Path.Combine(_targetFolder, $"{baseFileName}-p{pNum}-Words.txt");
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
