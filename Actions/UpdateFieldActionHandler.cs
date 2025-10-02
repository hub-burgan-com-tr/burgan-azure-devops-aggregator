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
    /// Work item field'ını günceller (conditional, calculated, append operations)
    /// </summary>
    public class UpdateFieldActionHandler : IActionHandler
    {
        private readonly AzureDevOpsClient _azureDevOpsClient;

        public UpdateFieldActionHandler(AzureDevOpsClient azureDevOpsClient)
        {
            _azureDevOpsClient = azureDevOpsClient ?? throw new ArgumentNullException(nameof(azureDevOpsClient));
        }

        public string ActionType => "UpdateField";

        public async Task ExecuteAsync(FlatWorkItemModel workItem, RuleAction action, ILogger logger)
        {
            if (workItem == null) throw new ArgumentNullException(nameof(workItem));
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            try
            {
                var fieldName = GetParameterValue(action.Parameters, "FieldName");
                var updateType = GetParameterValue(action.Parameters, "UpdateType"); // SET, APPEND, CALCULATE
                var newValue = GetParameterValue(action.Parameters, "Value");

                if (string.IsNullOrEmpty(fieldName))
                {
                    logger.LogWarning("⚠️ UpdateField action: FieldName parameter is required");
                    return;
                }

                logger.LogInformation($"🔄 UpdateField: {fieldName} ({updateType}) for WorkItem {workItem.WorkItemId}");

                // Get current field value
                var currentValue = GetCurrentFieldValue(workItem, fieldName);
                var finalValue = CalculateFinalValue(currentValue, newValue, updateType, workItem);

                if (finalValue != null)
                {
                    // Create field update dictionary
                    var fieldsToUpdate = new Dictionary<string, string>
                    {
                        [fieldName] = finalValue
                    };

                    // Azure DevOps field update
                    await _azureDevOpsClient.UpdateWorkItemFieldAsync(
                        workItem.WorkItemId,
                        fieldsToUpdate,
                        workItem.Fields["System_TeamProject"]?.ToString() ?? ""
                    );

                    logger.LogInformation($"✅ Field '{fieldName}' updated from '{currentValue}' to '{finalValue}'");
                }
                else
                {
                    logger.LogInformation($"⏩ UpdateField: No update needed for '{fieldName}'");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"❌ UpdateField action failed for WorkItem {workItem.WorkItemId}. ActionName: {action.ActionName}");
            }
        }

        private string GetCurrentFieldValue(FlatWorkItemModel workItem, string fieldName)
        {
            var key = fieldName.Replace(".", "_");
            
            if (workItem.Fields.ContainsKey(key))
            {
                return workItem.Fields[key]?.ToString() ?? string.Empty;
            }
            
            return string.Empty;
        }

        private string CalculateFinalValue(string currentValue, string newValue, string updateType, FlatWorkItemModel workItem)
        {
            return updateType?.ToUpper() switch
            {
                "SET" => newValue,
                "APPEND" => $"{currentValue}{newValue}",
                "PREPEND" => $"{newValue}{currentValue}",
                "CALCULATE" => CalculateValue(newValue, currentValue, workItem),
                _ => newValue // Default: SET
            };
        }

        private string CalculateValue(string formula, string currentValue, FlatWorkItemModel workItem)
        {
            try
            {
                if (formula == "EFFORT_TO_SIZE")
                {
                    return CalculateEffortToSize(workItem);
                }

                if (formula.StartsWith("ADD:") && double.TryParse(currentValue, out var current) && double.TryParse(formula.Substring(4), out var add))
                {
                    return (current + add).ToString();
                }
                
                if (formula.StartsWith("MULTIPLY:") && double.TryParse(currentValue, out current) && double.TryParse(formula.Substring(9), out var multiply))
                {
                    return (current * multiply).ToString();
                }

                return formula; // Fallback: return formula as literal value
            }
            catch
            {
                return currentValue; // Keep current value if calculation fails
            }
        }

        private string CalculateEffortToSize(FlatWorkItemModel workItem)
        {
            if (workItem.Fields.ContainsKey("Microsoft_VSTS_Scheduling_Effort") &&
                double.TryParse(workItem.Fields["Microsoft_VSTS_Scheduling_Effort"]?.ToString(), out var effort))
            {
                return effort switch
                {
                    <= 25 => "Küçük",
                    < 65 => "Orta", 
                    >= 65 => "Büyük",
                    _ => "Unknown"
                };
            }

            return "Unknown";
        }

        private string GetParameterValue(List<RuleActionParameter> parameters, string key)
        {
            return parameters?.FirstOrDefault(p => p.ParamKey == key)?.ParamValue ?? string.Empty;
        }
    }
} 