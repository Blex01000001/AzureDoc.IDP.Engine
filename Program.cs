using AzureDoc.IDP.Engine.Services;

namespace GoogleAIStudioProject
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine(">>>>START<<<<");


            var service = new PdfTableConversionService();


            var finalResult = await service.ExecuteAsync(@"C:\Users\01101006\Downloads\TEST\ALL.pdf");
            Console.WriteLine($"page\tNPD\tTag No.\tItem\tL\tMinConfidence");
            foreach (var item in finalResult)
            {
                Console.WriteLine($"{item.PageIndex}\t{item.Size}\t{item.TagNo}\t{item.Item}\t{item.L}\t{item.MinConfidence}");
            }



            //var fullText = await service.ReadAllTextAsync(@"H:\PDFs\32.pdf");
            //Console.WriteLine("--- PDF 文字提取結果 ---");
            //Console.WriteLine(fullText);


            Console.ReadKey();
        }
    }
}
