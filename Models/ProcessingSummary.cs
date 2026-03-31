using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzureDoc.IDP.Engine.Models
{
    /// <summary>
    /// 紀錄單次文件處理的完整摘要報告
    /// </summary>
    public class ProcessingSummary
    {
        // 1. 基本識別
        public Guid OperationId { get; set; } = Guid.NewGuid(); // 唯一識別碼 (UUID)
        public string FileName { get; set; }                   // 檔案名稱
        public string FilePath { get; set; }                   // 檔案完整路徑

        // 2. 統計數據
        public int TotalPages { get; set; }                    // 總頁數
        public int TotalRowCount => Data?.Count ?? 0;          // 總提取筆數 (唯讀屬性)

        // 3. 時間維度
        public DateTime StartTime { get; set; }                // 開始時間
        public DateTime EndTime { get; set; }                  // 完成時間
        public TimeSpan TotalDuration => EndTime - StartTime;  // 總花費時間

        // 4. 效能指標 (KPI)
        public double AveragePageTimeMs => TotalPages > 0
            ? TotalDuration.TotalMilliseconds / TotalPages
            : 0; // 每頁平均花費毫秒數

        // 5. 核心數據
        public List<ValveDimensionData> Data { get; set; } = new(); // 提取出的閥件數據

        // 6. 狀態資訊 (擴充性)
        public bool IsSuccess { get; set; } = true;
        public string ErrorMessage { get; set; }

        // 7. 信心分數分析 (選配)
        public float AverageConfidence => Data.Any() ? Data.Average(x => x.MinConfidence) : 0f;
    }
}
