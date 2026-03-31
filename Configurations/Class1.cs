using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AzureDoc.IDP.Engine.Configurations
{
    public static class ConfigLoader
    {
        public static AzureSettings LoadSettings(string configPath = "appsettings.json")
        {
            var json = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize<Dictionary<string, AzureSettings>>(json)["AzureSettings"];
        }
    }
}
