using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzureDoc.IDP.Engine.Models
{
    public class ValveDimensionData
    {
        public string FileName { get; set; }
        public int PageIndex { get; set; }

        // 資料欄位
        public string Size { get; set; }
        public string L { get; set; }
        public string TagNo { get; set; }
        public string Item { get; set; }

        // 信心分數欄位 (Azure 回傳值為 0.0 ~ 1.0)
        public float SizeConfidence { get; set; }
        public float LConfidence { get; set; }
        public float TagNoConfidence { get; set; }
        public float ItemConfidence { get; set; }

        // 輔助屬性：該筆資料是否可靠 (例如平均信心 > 0.8)
        public float MinConfidence => new[] { SizeConfidence, LConfidence, TagNoConfidence, ItemConfidence }.Min();
    }
}
