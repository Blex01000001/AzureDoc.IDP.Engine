using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using AzureDoc.IDP.Engine.Configurations;
using AzureDoc.IDP.Engine.Helpers;
using AzureDoc.IDP.Engine.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System.Text;

namespace AzureDoc.IDP.Engine.Services
{
    public class PdfTableConversionService
    {
        private readonly DocumentAnalysisClient _client;
        private readonly ValveTableParser _parser;

        public PdfTableConversionService()
        {
            var settings = ConfigLoader.LoadSettings();
            _client = new DocumentAnalysisClient(new Uri(settings.Endpoint), new AzureKeyCredential(settings.ApiKey));
            _parser = new ValveTableParser();
        }

        public async Task<List<ValveDimensionData>> ExecuteAsync(string filePath)
        {
            var allResults = new List<ValveDimensionData>();
            string fileNameOnly = Path.GetFileNameWithoutExtension(filePath);

            using var inputDocument = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);

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

                // 解析資料
                var pageData = _parser.Parse(operation.Value, fileNameOnly, pageNumber);
                allResults.AddRange(pageData);

                // 免費版頻率限制處理
                if (i < inputDocument.PageCount - 1)
                {
                    await Task.Delay(4100);
                }
            }
            return allResults;
        }

        public async Task<string> ReadAllTextAsync(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"找不到檔案: {filePath}");

            using var stream = new FileStream(filePath, FileMode.Open);

            // 1. 呼叫 Azure Read 模型 (prebuilt-read)
            // 注意：如果是免費版，通常只會回傳前 2 頁的結果
            var operation = await _client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-read", stream);
            AnalyzeResult result = operation.Value;

            // 2. 使用 StringBuilder 高效拼接文字
            var sb = new StringBuilder();

            // 依照頁面、行、內容的順序提取
            foreach (var page in result.Pages)
            {
                foreach (var line in page.Lines)
                {
                    sb.AppendLine(line.Content);
                }
            }

            return sb.ToString();
        }
    }





    // =============================================OLD==========================================================
    //
    //public class PdfTableConversionService
    //{
    //    List<ValveDimensionData> finalResult = new List<ValveDimensionData>();
    //    string endpoint = "https://pdftablerecognition.cognitiveservices.azure.com/";
    //    string apiKey = "api_key";
    //    public async Task<List<ValveDimensionData>> ProcessPdfPageByPageAsync(string filePath)
    //    {
    //        // 1. C# 讀取一整份 PDF
    //        using (PdfDocument inputDocument = PdfReader.Open(filePath, PdfDocumentOpenMode.Import))
    //        {
    //            int pageCount = inputDocument.PageCount;
    //            string fileNameOnly = Path.GetFileNameWithoutExtension(filePath);

    //            for (int i = 0; i < pageCount; i++)
    //            {
    //                // 4. 每一頁的 index 存成變數 (通常使用 1-based index 存入物件)
    //                int currentPageIndex = i + 1;
    //                Console.WriteLine($"正在處理檔案: {fileNameOnly}, 第 {currentPageIndex} / {pageCount} 頁...");

    //                // 2. 每一頁都產生一個獨立的 PDF 串流
    //                using (PdfDocument outputDocument = new PdfDocument())
    //                {
    //                    // 將當前頁面加入新的文件
    //                    outputDocument.AddPage(inputDocument.Pages[i]);

    //                    using (MemoryStream pageStream = new MemoryStream())
    //                    {
    //                        // 儲存為獨立的 PDF 串流
    //                        outputDocument.Save(pageStream, false);
    //                        pageStream.Position = 0;

    //                        // --- 呼叫 API 的區塊 ---
    //                        // 這裡帶入您已經寫好的 API 呼叫邏輯，傳入 pageStream 與 currentPageIndex
    //                        // 例如: await YourAnalyzeMethod(pageStream, currentPageIndex, fileNameOnly);
    //                        await Analyze(pageStream, currentPageIndex, fileNameOnly);
    //                        // -----------------------
    //                    }
    //                }

    //                // 3. 每一頁發 API 間隔時間為 4 秒 (解決免費版 15 RPM 的限制)
    //                if (i < pageCount - 1) // 最後一頁處理完後不需等待
    //                {
    //                    Console.WriteLine("等待 4 秒以符合免費版配額限制...");
    //                    await Task.Delay(4000);
    //                }
    //            }
    //        }
    //        return finalResult;
    //    }

    //    public async Task Analyze(Stream stream, int currentPageIndex, string fileNameOnly)
    //    {
    //        string[] targetHeaders = { "SIZE", "Tag no.", "ITEM" };

    //        var client = new DocumentAnalysisClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
    //        AnalyzeDocumentOperation operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-layout", stream);
    //        AnalyzeResult analyzeResult = operation.Value;

    //        foreach (DocumentTable table in analyzeResult.Tables)
    //        {
    //            // 檢查此表格是否有任何「表頭單元格」包含上述關鍵字
    //            //[cite_start]// 依據 AzureOutput.txt，尺寸表的 Row 0 包含了 SIZE , Tag no. , ITEM 
    //            bool isTargetTable = table.Cells.Any(cell =>
    //                (cell.Kind == DocumentTableCellKind.ColumnHeader || cell.RowIndex == 0) &&
    //                targetHeaders.Any(header => cell.Content.Contains(header, StringComparison.OrdinalIgnoreCase))
    //            );

    //            // 如果不是我們要的表格，直接跳過 (其餘表格捨棄)
    //            if (!isTargetTable) continue;


    //            // 檢查標題列，找出目標欄位的 Index
    //            var headerCells = table.Cells.Where(c => c.RowIndex == 0).ToList();

    //            // ... 在迴圈內部 ...

    //            // 1. SIZE: 模糊匹配 "SIZE" (不分大小寫)
    //            int? colSize = headerCells.FirstOrDefault(c =>
    //                c.Content.Contains("SIZE", StringComparison.OrdinalIgnoreCase))?.ColumnIndex;

    //            // 2. L: 必須精確匹配 "L" (不分大小寫)，避免抓到包含 L 的其他單字 (如 Shell)
    //            int? colL = headerCells.FirstOrDefault(c =>
    //                c.Content.Equals("L", StringComparison.OrdinalIgnoreCase))?.ColumnIndex;

    //            // 3. Tag no: 模糊匹配 "Tag" (不分大小寫)
    //            int? colTag = headerCells.FirstOrDefault(c =>
    //                c.Content.Contains("Tag", StringComparison.OrdinalIgnoreCase))?.ColumnIndex;

    //            // 4. ITEM: 針對 Item, Item no, ITEM No 等多種組合進行模糊匹配
    //            int? colItem = headerCells.FirstOrDefault(c =>
    //                c.Content.StartsWith("Item", StringComparison.OrdinalIgnoreCase))?.ColumnIndex;



    //            // 如果關鍵欄位都存在，才處理該表格
    //            if (colSize.HasValue && colL.HasValue && colTag.HasValue && colItem.HasValue)
    //            {
    //                // 從第 1 列開始讀取數據 (跳過第 0 列標題)
    //                for (int i = 1; i < table.RowCount; i++)
    //                {
    //                    var rowCells = table.Cells.Where(c => c.RowIndex == i).ToList();

    //                    var entry = new ValveDimensionData
    //                    {
    //                        FileName = fileNameOnly,
    //                        // 使用自定義方法 CleanValue 來去除 "|"
    //                        Size = CleanValue(rowCells.FirstOrDefault(c => c.ColumnIndex == colSize)?.Content),
    //                        L = CleanValue(rowCells.FirstOrDefault(c => c.ColumnIndex == colL)?.Content),
    //                        TagNo = CleanValue(rowCells.FirstOrDefault(c => c.ColumnIndex == colTag)?.Content),
    //                        Item = CleanValue(rowCells.FirstOrDefault(c => c.ColumnIndex == colItem)?.Content)
    //                    };

    //                    finalResult.Add(entry);
    //                }
    //            }
    //        }

    //    }
    //    private string CleanValue(string input)
    //    {
    //        if (string.IsNullOrEmpty(input)) return "";
    //        return input.Replace("|", "").Trim();
    //    }
    //}
}
