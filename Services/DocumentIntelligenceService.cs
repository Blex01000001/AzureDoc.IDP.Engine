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
        private readonly string _endpoint;
        private readonly string _apiKey;
        private string _targetFolder;
        private readonly DocumentAnalysisClient _client;
        private DocumentResponse _documentResponse = new DocumentResponse();
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
        public DocumentIntelligenceService(string endpoint, string apiKey)
        {
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _client = new DocumentAnalysisClient(new Uri(_endpoint), new AzureKeyCredential(_apiKey));
            _documentResponse.StartTime = DateTime.Now;
            string rootTempPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            _targetFolder = Path.Combine(rootTempPath, outputFolderName);
            if (!Directory.Exists(_targetFolder)) Directory.CreateDirectory(_targetFolder);
            _documentResponse.TargetFolder = _targetFolder;
        }

        /// <summary>
        /// 高效能並行提取數據 (適用於 S0 付費版)
        /// </summary>
        /// <param name="filePath">PDF完整路徑</param>
        /// <param name="maxDegreeOfParallelism">最大並行數</param>
        /// <returns></returns>
        public async Task<DocumentResponse> AnalyzeInParallelAsync(string filePath, int maxDegreeOfParallelism = 10)
        {
            _documentResponse.FileName = Path.GetFileName(filePath);
            _documentResponse.FilePath = filePath;
            var sw = Stopwatch.StartNew();
            string fileNameOnly = Path.GetFileNameWithoutExtension(filePath);

            using var inputDocument = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
            int totalPages = inputDocument.PageCount;
            _documentResponse.TotalPages = totalPages;

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
                        // 再過 TokenBucket (確保每秒請求數受控)
                        using var lease = await _payLimiter.AcquireAsync(1);
                        if (!lease.IsAcquired) return;
                        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [發送] 頁面 {pageNumber}");
                        // 準備單頁 Stream
                        using var ms = PrepareSinglePageStream(filePath, pageNumber - 1);
                        // 呼叫 Azure API
                        AnalyzeDocumentOperation operation = await _client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-layout", ms);
                        _documentResponse.Results.Add(pageNumber, operation.Value);
                        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [完成] 頁面 {pageNumber}");
                    }
                    catch (Exception ex)
                    {
                        _documentResponse.IsSuccess = false;
                        _documentResponse.ErrorMessage = ex.Message;
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }
            await Task.WhenAll(tasks);
            sw.Stop();
            _documentResponse.EndTime = DateTime.Now;
            return _documentResponse;
        }
        /// <summary>
        /// 低效能循序提取數據 (適用於 F0 免費版)
        /// </summary>
        /// <param name="filePath">PDF完整路徑</param>
        /// <returns></returns>
        public async Task<DocumentResponse> AnalyzeInSequentialAsync(string filePath)
        {
            _documentResponse.FileName = Path.GetFileName(filePath);
            _documentResponse.FilePath = filePath;
            var sw = Stopwatch.StartNew();
            string fileNameOnly = Path.GetFileNameWithoutExtension(filePath);

            using var inputDocument = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
            _documentResponse.TotalPages = inputDocument.PageCount;

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
                _documentResponse.Results.Add(pageNumber, operation.Value);

            }
            sw.Stop();
            _documentResponse.EndTime = DateTime.Now;
            return _documentResponse;
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
    }
}
