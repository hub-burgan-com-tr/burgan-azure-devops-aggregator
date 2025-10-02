using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BurganAzureDevopsAggregator.Business;
using BurganAzureDevopsAggregator.Models;
using Microsoft.Extensions.Logging;

namespace BurganAzureDevopsAggregator.Actions
{
 public class ChangeStateActionHandler : IActionHandler
{
    private readonly AzureDevOpsClient _azureDevOpsClient;

    public ChangeStateActionHandler(AzureDevOpsClient azureDevOpsClient)
    {
        _azureDevOpsClient = azureDevOpsClient ?? throw new ArgumentNullException(nameof(azureDevOpsClient));
    }

    public string ActionType => "ChangeState";

    public async Task ExecuteAsync(FlatWorkItemModel workItem, RuleAction action, ILogger logger)
    {
        if (workItem == null) throw new ArgumentNullException(nameof(workItem));
        if (action == null) throw new ArgumentNullException(nameof(action));
        if (logger == null) throw new ArgumentNullException(nameof(logger));

        var fieldsToUpdate = action.Parameters
            .ToDictionary(p => p.ParamKey, p => p.ParamValue);

        try
        {
            await _azureDevOpsClient.UpdateWorkItemFieldAsync(
                workItem.WorkItemId,
                fieldsToUpdate,
                workItem.Fields["System_TeamProject"].ToString()
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"❌ WorkItem {workItem.WorkItemId} durumu güncellenirken hata oluştu. ActionName: {action.ActionName}");
        }
    }
}

}
