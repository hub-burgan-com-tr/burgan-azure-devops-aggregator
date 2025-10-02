using BurganAzureDevopsAggregator.Business;
using BurganAzureDevopsAggregator.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BurganAzureDevopsAggregator.Actions
{
    /// <summary>
    /// Risk skoru ve seviyesi hesaplama için özel action handler
    /// </summary>
    public class RiskCalculationActionHandler : IActionHandler
    {
        private readonly AzureDevOpsClient _azureDevOpsClient;

        // Risk seviyesi matris tablosu
        private static readonly string[,] RiskSeviyesiTablosu = new string[5, 5]
        {
            { "Çok Düşük", "Düşük", "Düşük", "Düşük", "Orta" },
            { "Düşük", "Düşük", "Düşük", "Orta", "Orta" },
            { "Düşük", "Düşük", "Orta", "Orta", "Yüksek" },
            { "Düşük", "Orta", "Orta", "Yüksek", "Yüksek" },
            { "Orta", "Orta", "Yüksek", "Yüksek", "Çok Yüksek" }
        };

        public RiskCalculationActionHandler(AzureDevOpsClient azureDevOpsClient)
        {
            _azureDevOpsClient = azureDevOpsClient ?? throw new ArgumentNullException(nameof(azureDevOpsClient));
        }

        public string ActionType => "RiskCalculation";

        public async Task ExecuteAsync(FlatWorkItemModel workItem, RuleAction action, ILogger logger)
        {
            if (workItem == null) throw new ArgumentNullException(nameof(workItem));
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            try
            {
                // Field'ları al
                var riskFieldKey = "Microsoft_VSTS_Common_Risk";
                var severityFieldKey = "Microsoft_VSTS_Common_Severity";

                var riskMetin = GetFieldValue(workItem, riskFieldKey);
                var severityMetin = GetFieldValue(workItem, severityFieldKey);

                logger.LogInformation($"🧮 RiskCalculation: Processing WorkItem {workItem.WorkItemId}");
                logger.LogInformation($"   Risk: '{riskMetin}', Severity: '{severityMetin}'");

                // String'lerden integer değerleri parse et
                if (TryParseRiskValue(riskMetin, out int olasilik) && 
                    TryParseRiskValue(severityMetin, out int hasarEtkisi))
                {
                    // Risk skorunu hesapla
                    int riskSkoru = olasilik * hasarEtkisi;

                    // Matrix'ten risk seviyesini al
                    string riskSeviyesi = GetRiskSeviyesi(olasilik, hasarEtkisi);

                    logger.LogInformation($"   Calculated: Score={riskSkoru}, Level={riskSeviyesi}");

                    // Field update'leri hazırla
                    var fieldsToUpdate = new Dictionary<string, string>
                    {
                        ["Talep.RiskSkoru"] = riskSkoru.ToString(),
                        ["Talep.RiskSeviyesi"] = riskSeviyesi
                    };

                    // Azure DevOps'ta field'ları güncelle
                    await _azureDevOpsClient.UpdateWorkItemFieldAsync(
                        workItem.WorkItemId,
                        fieldsToUpdate,
                        workItem.Fields["System_TeamProject"]?.ToString() ?? ""
                    );

                    logger.LogInformation($"✅ Risk calculation completed for WorkItem {workItem.WorkItemId}");
                }
                else
                {
                    logger.LogWarning($"⚠️ Unable to parse risk values. Risk: '{riskMetin}', Severity: '{severityMetin}'");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"❌ RiskCalculation action failed for WorkItem {workItem.WorkItemId}");
            }
        }

        private string GetFieldValue(FlatWorkItemModel workItem, string fieldKey)
        {
            if (workItem.Fields.ContainsKey(fieldKey))
            {
                return workItem.Fields[fieldKey]?.ToString() ?? string.Empty;
            }
            return string.Empty;
        }

        private bool TryParseRiskValue(string riskText, out int value)
        {
            value = 0;
            
            if (string.IsNullOrWhiteSpace(riskText))
                return false;

            // "3 - Orta" formatından "3" kısmını al
            var parts = riskText.Split(' ');
            if (parts.Length > 0)
            {
                return int.TryParse(parts[0], out value) && value >= 1 && value <= 5;
            }

            return false;
        }

        private string GetRiskSeviyesi(int olasilik, int hasarEtkisi)
        {
            // 1-5 range check
            if (olasilik >= 1 && olasilik <= 5 && hasarEtkisi >= 1 && hasarEtkisi <= 5)
            {
                return RiskSeviyesiTablosu[olasilik - 1, hasarEtkisi - 1];
            }

            return "Unknown";
        }
    }
} 