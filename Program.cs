using AzureDoc.IDP.Engine.Configurations;
using AzureDoc.IDP.Engine.Services;

namespace GoogleAIStudioProject
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine(">>>>Doc Intelligence Service START<<<<");
            //string filePath = @"C:\Users\01101006\Downloads\TEST\ALL.pdf";
            string filePath = @"H:\PDFs\M106.pdf";

            IDocumentIntelligenceService docIntelligenceService = new DocumentIntelligenceService(ConfigLoader.LoadSettings());
            var ProcessingSummary = await docIntelligenceService.AnalyzeInParallelAsync(filePath);


            Console.WriteLine($"page\tNPD\tTag No.\tItem\tL\tMinConfidence");
            foreach (var item in ProcessingSummary.Data)
            {
                Console.WriteLine($"{item.PageIndex}\t{item.Size}\t{item.TagNo}\t{item.Item}\t{item.L}\t{item.MinConfidence}");
            }

            Console.ReadKey();
        }
    }
}
