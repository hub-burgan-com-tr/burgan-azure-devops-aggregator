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
    /// Work item'ı yeni state'e geçirir ve optional comment ekler
    /// </summary>
    public class TransitionToStateActionHandler : IActionHandler
    {
        private readonly AzureDevOpsClient _azureDevOpsClient;

        public TransitionToStateActionHandler(AzureDevOpsClient azureDevOpsClient)
        {
            _azureDevOpsClient = azureDevOpsClient ?? throw new ArgumentNullException(nameof(azureDevOpsClient));
        }

        public string ActionType => "TransitionToState";

        public async Task ExecuteAsync(FlatWorkItemModel workItem, RuleAction action, ILogger logger)
        {
            if (workItem == null) throw new ArgumentNullException(nameof(workItem));
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            try
            {
                var newState = GetParameterValue(action.Parameters, "NewState");
                var comment = GetParameterValue(action.Parameters, "Comment");
                var reason = GetParameterValue(action.Parameters, "Reason");

                if (string.IsNullOrEmpty(newState))
                {
                    logger.LogWarning("⚠️ TransitionToState action: NewState parameter is required");
                    return;
                }

                logger.LogInformation($"🔄 TransitionToState: WorkItem {workItem.WorkItemId} → '{newState}'");

                // Create state change fields
                var fieldsToUpdate = new Dictionary<string, string>
                {
                    ["System.State"] = newState
                };

                if (!string.IsNullOrEmpty(reason))
                {
                    fieldsToUpdate["System.Reason"] = reason;
                }

                // State change
                await _azureDevOpsClient.UpdateWorkItemFieldAsync(
                    workItem.WorkItemId,
                    fieldsToUpdate,
                    workItem.Fields["System_TeamProject"]?.ToString() ?? ""
                );

                logger.LogInformation($"✅ State changed to '{newState}' for WorkItem {workItem.WorkItemId}");

                // Add comment if provided
                if (!string.IsNullOrEmpty(comment))
                {
                    try
                    {
                        await _azureDevOpsClient.AddCommentToWorkItemAsync(
                            workItem.WorkItemId, 
                            comment, 
                            workItem.Fields["System_TeamProject"]?.ToString() ?? ""
                        );
                        logger.LogInformation($"✅ Comment added: '{comment}'");
                    }
                    catch (Exception commentEx)
                    {
                        logger.LogWarning(commentEx, $"⚠️ State changed but comment failed: {commentEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"❌ TransitionToState action failed for WorkItem {workItem.WorkItemId}. ActionName: {action.ActionName}");
            }
        }

        private string GetParameterValue(List<RuleActionParameter> parameters, string key)
        {
            return parameters?.FirstOrDefault(p => p.ParamKey == key)?.ParamValue ?? string.Empty;
        }
    }
} 