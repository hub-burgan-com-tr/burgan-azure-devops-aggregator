using System.Linq;
using System.Threading.Tasks;
using BurganAzureDevopsAggregator.Business;
using BurganAzureDevopsAggregator.Models;
using Microsoft.Extensions.Logging;

namespace BurganAzureDevopsAggregator.Actions
{
    public class AddCommentActionHandler : IActionHandler
    {
        private readonly AzureDevOpsClient _azureDevOpsClient;

        public AddCommentActionHandler(AzureDevOpsClient azureDevOpsClient)
        {
            _azureDevOpsClient = azureDevOpsClient;
        }

        public string ActionType => "AddComment";

        public async Task ExecuteAsync(FlatWorkItemModel workItem, RuleAction action, ILogger logger)
        {
            var commentParam = action.Parameters.FirstOrDefault(p => p.ParamKey == "CommentText");

            if (commentParam == null || string.IsNullOrWhiteSpace(commentParam.ParamValue))
            {
                logger.LogWarning($"💬 Yorum parametresi bulunamadı veya boş. Work item ID: {workItem.WorkItemId}");
                return;
            }

            try
            {
                await _azureDevOpsClient.AddCommentToWorkItemAsync(workItem.WorkItemId, commentParam.ParamValue,workItem.Fields["System_TeamProject"].ToString());
                logger.LogInformation($"💬 Yorum eklendi. Work item ID: {workItem.WorkItemId}");
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, $"❌ Yorum ekleme başarısız oldu. Work item ID: {workItem.WorkItemId}");
            }
        }
    }
}
