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
    /// Work item field'ını set eder (yeni değer atar)
    /// </summary>
    public class SetFieldActionHandler : IActionHandler
    {
        private readonly AzureDevOpsClient _azureDevOpsClient;

        public SetFieldActionHandler(AzureDevOpsClient azureDevOpsClient)
        {
            _azureDevOpsClient = azureDevOpsClient ?? throw new ArgumentNullException(nameof(azureDevOpsClient));
        }

        public string ActionType => "SetField";

        public async Task ExecuteAsync(FlatWorkItemModel workItem, RuleAction action, ILogger logger)
        {
            if (workItem == null) throw new ArgumentNullException(nameof(workItem));
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            try
            {
                var fieldName = GetParameterValue(action.Parameters, "FieldName");
                var fieldValue = GetParameterValue(action.Parameters, "FieldValue");

                if (string.IsNullOrEmpty(fieldName))
                {
                    logger.LogWarning("⚠️ SetField action: FieldName parameter is required");
                    return;
                }

                logger.LogInformation($"🔧 SetField: {fieldName} = '{fieldValue}' for WorkItem {workItem.WorkItemId}");

                // Process dynamic field value (e.g., {CreatedBy})
                var processedValue = ProcessDynamicValue(fieldValue, workItem);

                // Create field update dictionary
                var fieldsToUpdate = new Dictionary<string, string>
                {
                    [fieldName] = processedValue
                };

                // Azure DevOps field update
                await _azureDevOpsClient.UpdateWorkItemFieldAsync(
                    workItem.WorkItemId,
                    fieldsToUpdate,
                    workItem.Fields["System_TeamProject"]?.ToString() ?? ""
                );

                logger.LogInformation($"✅ Field '{fieldName}' successfully set to '{processedValue}'");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"❌ SetField action failed for WorkItem {workItem.WorkItemId}. ActionName: {action.ActionName}");
            }
        }

        private string GetParameterValue(List<RuleActionParameter> parameters, string key)
        {
            return parameters?.FirstOrDefault(p => p.ParamKey == key)?.ParamValue ?? string.Empty;
        }

        private string ProcessDynamicValue(string value, FlatWorkItemModel workItem)
        {
            // Handle dynamic placeholders like {CreatedBy}
            if (value.StartsWith("{") && value.EndsWith("}"))
            {
                var fieldName = value.Substring(1, value.Length - 2);
                
                // Handle special operations like {Talep.TalepHazirEden.Split}
                if (fieldName.EndsWith(".Split"))
                {
                    var baseFieldName = fieldName.Replace(".Split", "");
                    var fieldKey = baseFieldName.Replace(".", "_");
                    
                    if (workItem.Fields.ContainsKey(fieldKey))
                    {
                        var fieldValue = workItem.Fields[fieldKey]?.ToString() ?? "";
                        // Split by '<' and take first part (like original XML rule)
                        return fieldValue.Split('<')[0];
                    }
                }
                else
                {
                    // Normal field placeholder
                    var fieldKey = fieldName.Replace(".", "_");
                    
                    if (workItem.Fields.ContainsKey(fieldKey))
                    {
                        return workItem.Fields[fieldKey]?.ToString() ?? "";
                    }
                }
            }

            return value;
        }
    }
} 