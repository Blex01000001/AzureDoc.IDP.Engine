using AzureDoc.IDP.Engine.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzureDoc.IDP.Engine.Services
{
    public interface IDocumentIntelligenceService
    {
        public Task<ProcessingSummary> AnalyzeInParallelAsync(string filePath, int maxDegreeOfParallelism = 10);
        public Task<ProcessingSummary> AnalyzeInSequentialAsync(string filePath);

    }
}
