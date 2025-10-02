using BurganAzureDevopsAggregator.Business;
using BurganAzureDevopsAggregator.Models;
using Microsoft.Extensions.Logging;

namespace BurganAzureDevopsAggregator.Actions
{
    public class ExecuteXmlCalculationActionHandler : IActionHandler
    {
        private readonly XmlRuleProcessor _xmlRuleProcessor;

        public ExecuteXmlCalculationActionHandler(XmlRuleProcessor xmlRuleProcessor)
        {
            _xmlRuleProcessor = xmlRuleProcessor;
        }

        public string ActionType => "ExecuteXmlCalculation";

        public async Task ExecuteAsync(FlatWorkItemModel workItem, RuleAction action, ILogger logger)
        {
            try
            {
                // Action parameters'dan XML rule bilgilerini al
                var xmlRuleName = GetParameterValue(action, "XmlRuleName");
                var csharpCode = GetParameterValue(action, "CSharpCode");
                // AppliesTo artık parameter'da değil, RuleModel.AppliesTo'da

                if (string.IsNullOrEmpty(xmlRuleName) || string.IsNullOrEmpty(csharpCode))
                {
                    logger.LogWarning($"⚠️ XML Calculation action missing required parameters. Rule: {xmlRuleName}");
                    return;
                }

                // XML calculation rule oluştur
                var xmlRule = new XmlCalculationRule
                {
                    Name = xmlRuleName,
                    CSharpCode = csharpCode,
                    AppliesTo = "All" // AppliesTo kontrolü RulesEngineService'te yapılıyor
                };

                // XML rule'ı execute et
                var success = await _xmlRuleProcessor.ExecuteXmlCalculationRuleAsync(xmlRule, workItem);

                if (success)
                {
                    logger.LogInformation($"✅ XML Calculation '{xmlRuleName}' executed successfully for WorkItem {workItem.WorkItemId}");
                }
                else
                {
                    logger.LogWarning($"⚠️ XML Calculation '{xmlRuleName}' execution failed for WorkItem {workItem.WorkItemId}");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"❌ Error executing XML Calculation action for WorkItem {workItem.WorkItemId}");
            }
        }

        private string GetParameterValue(RuleAction action, string paramKey)
        {
            return action.Parameters?.FirstOrDefault(p => p.ParamKey == paramKey)?.ParamValue;
        }
    }
} 