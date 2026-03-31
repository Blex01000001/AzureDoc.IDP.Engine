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
            return result.Pages[0].Words.First(x => x.Content == cell.Content && x.Span.Index == cell.Spans[0].Index).Confidence;
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
