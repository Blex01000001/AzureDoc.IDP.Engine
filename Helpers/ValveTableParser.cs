using Azure.AI.FormRecognizer.DocumentAnalysis;
using AzureDoc.IDP.Engine.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzureDoc.IDP.Engine.Helpers
{
    public class ValveTableParser
    {

        public List<ValveDimensionData> Parse(AnalyzeResult result, string fileName, int pageIndex)
        {
            //PrintWords(result);
            //SaveAnalysisToTxt(result, "log/" + fileName + ".pdf");
            var list = new List<ValveDimensionData>();
            foreach (var table in result.Tables)
            {
                var headers = table.Cells.Where(c => c.RowIndex == 0).ToList();

                // (找到欄位索引的邏輯維持不變...)
                int? colSize = headers.FirstOrDefault(c => c.Content.Contains("SIZE", StringComparison.OrdinalIgnoreCase))?.ColumnIndex;
                int? colL = headers.FirstOrDefault(c => c.Content.Trim().Equals("L", StringComparison.OrdinalIgnoreCase))?.ColumnIndex;
                int? colTag = headers.FirstOrDefault(c => c.Content.Contains("Tag", StringComparison.OrdinalIgnoreCase))?.ColumnIndex;
                int? colItem = headers.FirstOrDefault(c => c.Content.StartsWith("Item", StringComparison.OrdinalIgnoreCase))?.ColumnIndex;

                if (colSize.HasValue && colL.HasValue && colTag.HasValue && colItem.HasValue)
                {
                    for (int i = 1; i < table.RowCount; i++)
                    {
                        var row = table.Cells.Where(c => c.RowIndex == i).ToList();

                        var sCell = row.FirstOrDefault(c => c.ColumnIndex == colSize);
                        var lCell = row.FirstOrDefault(c => c.ColumnIndex == colL);
                        var tCell = row.FirstOrDefault(c => c.ColumnIndex == colTag);
                        var iCell = row.FirstOrDefault(c => c.ColumnIndex == colItem);

                        list.Add(new ValveDimensionData
                        {
                            FileName = fileName,
                            PageIndex = pageIndex,

                            Size = CleanValue(sCell?.Content),
                            L = CleanValue(lCell?.Content),
                            TagNo = CleanValue(tCell?.Content),
                            Item = CleanValue(iCell?.Content),

                            // 信心分數
                            SizeConfidence = GetConfidence(result, sCell),
                            LConfidence = GetConfidence(result, lCell),
                            TagNoConfidence = GetConfidence(result, tCell),
                            ItemConfidence = GetConfidence(result, iCell)

                        });
                    }
                }
            }
            return list;
        }


        private float GetConfidence(AnalyzeResult result, DocumentTableCell cell)
        {
            var word = result.Pages[0].Words.FirstOrDefault(x => x.Content == cell.Content && x.Span.Index == cell.Spans[0].Index);
            if (word == null)
            {
                // 這裡可以下中斷點，觀察 cell.Content 到底長什麼樣子
                Console.WriteLine($"[Debug] 找不到對應單字: {cell.Content}");
                return 0f;
            }
            return word.Confidence;
        }

        private void PrintWords(AnalyzeResult result)
        {
            Console.WriteLine("\n===============================================================");
            var words = result.Pages[0].Words;
            for (int i = 0; i < result.Pages[0].Words.Count - 1; i++)
            {
                var word = words[i];
                Console.WriteLine($"{i}\t{word.Confidence}\t{word.Content}\t{word.Span.Index}");
            }
            Console.WriteLine("===============================================================\n");
        }

        private void SaveAnalysisToTxt(AnalyzeResult result, string pdfFilePath)
        {
            // 1. 產生對應的 TXT 檔名 (例如 48.pdf -> 48.txt)
            string txtPath = Path.ChangeExtension(pdfFilePath, ".txt");

            using (StreamWriter writer = new StreamWriter(txtPath, false, Encoding.UTF8))
            {
                writer.WriteLine($"文件分析報告: {Path.GetFileName(pdfFilePath)}");
                writer.WriteLine($"生成時間: {DateTime.Now}");
                writer.WriteLine("===============================================================");

                foreach (var page in result.Pages)
                {
                    writer.WriteLine($"\n【 第 {page.PageNumber} 頁 】");

                    // --- 區段一：印出 Lines (整行內容，適合人類閱讀) ---
                    writer.WriteLine("\n[Lines 概覽]");
                    foreach (var line in page.Lines)
                    {
                        writer.WriteLine(line.Content);
                    }

                    // --- 區段二：印出 Words 細節 (包含信心分數與 Span，適合除錯) ---
                    writer.WriteLine("\n[Words 詳細數據]");
                    writer.WriteLine("Index\tConfidence\tContent\t\tOffset\tLength");
                    writer.WriteLine("---------------------------------------------------------------");

                    for (int i = 0; i < page.Words.Count; i++)
                    {
                        var word = page.Words[i];
                        // 使用 :F4 限制信心分數到小數點後四位，排版較整齊
                        writer.WriteLine($"{i}\t{word.Confidence:F4}\t\t{word.Content}\t\t{word.Span.Index}\t{word.Span.Length}");
                    }

                    writer.WriteLine("---------------------------------------------------------------");
                }
            }

            //Console.WriteLine($"[系統] 分析結果已存至: {txtPath}");
        }
        //public List<ValveDimensionData> Parse(AnalyzeResult result, string fileName, int pageIndex)
        //{
        //    var list = new List<ValveDimensionData>();
        //    foreach (var table in result.Tables)
        //    {
        //        var headers = table.Cells.Where(c => c.RowIndex == 0).ToList();

        //        // 模糊判斷欄位索引
        //        int? colSize = headers.FirstOrDefault(c => c.Content.Contains("SIZE", StringComparison.OrdinalIgnoreCase))?.ColumnIndex;
        //        int? colL = headers.FirstOrDefault(c => c.Content.Trim().Equals("L", StringComparison.OrdinalIgnoreCase))?.ColumnIndex;
        //        int? colTag = headers.FirstOrDefault(c => c.Content.Contains("Tag", StringComparison.OrdinalIgnoreCase))?.ColumnIndex;
        //        int? colItem = headers.FirstOrDefault(c => c.Content.StartsWith("Item", StringComparison.OrdinalIgnoreCase))?.ColumnIndex;

        //        if (colSize.HasValue && colL.HasValue && colTag.HasValue && colItem.HasValue)
        //        {
        //            for (int i = 1; i < table.RowCount; i++)
        //            {
        //                var row = table.Cells.Where(c => c.RowIndex == i).ToList();
        //                list.Add(new ValveDimensionData
        //                {
        //                    // 提取內容
        //                    FileName = fileName,
        //                    PageIndex = pageIndex,
        //                    Size = CleanValue(row.FirstOrDefault(c => c.ColumnIndex == colSize)?.Content),
        //                    L = CleanValue(row.FirstOrDefault(c => c.ColumnIndex == colL)?.Content),
        //                    TagNo = CleanValue(row.FirstOrDefault(c => c.ColumnIndex == colTag)?.Content),
        //                    Item = CleanValue(row.FirstOrDefault(c => c.ColumnIndex == colItem)?.Content),
        //                });
        //            }
        //        }
        //    }
        //    return list;
        //}
        public string CleanValue(string input) => input?.Replace("|", "").Trim() ?? "";

    }
}
