using System.Threading.Tasks;
using BurganAzureDevopsAggregator.Models;
using Microsoft.Extensions.Logging;

namespace BurganAzureDevopsAggregator.Actions
{
    public interface IActionHandler
    {
        string ActionType { get; }

        Task ExecuteAsync(FlatWorkItemModel workItem, RuleAction action, ILogger logger);
    }
}
