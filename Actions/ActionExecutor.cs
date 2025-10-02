using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BurganAzureDevopsAggregator.Models;
using Microsoft.Extensions.Logging;

namespace BurganAzureDevopsAggregator.Actions
{
    public class ActionExecutor
    {
        private readonly Dictionary<string, IActionHandler> _handlers;
        private readonly ILogger<ActionExecutor> _logger;

        public ActionExecutor(IEnumerable<IActionHandler> handlers, ILogger<ActionExecutor> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (handlers == null) throw new ArgumentNullException(nameof(handlers));

            _handlers = new Dictionary<string, IActionHandler>(StringComparer.OrdinalIgnoreCase);

            foreach (var handler in handlers)
            {
                if (handler == null) continue;

                var actionType = handler.ActionType;
                if (!_handlers.ContainsKey(actionType))
                {
                    _handlers.Add(actionType, handler);
                }
                else
                {
                    _logger.LogWarning($"⚠️ Aynı aksiyon tipi '{actionType}' için birden fazla handler tespit edildi. İlk handler kullanılacak.");
                }
            }
        }

        public async Task ExecuteActionsAsync(List<RuleAction> actions, FlatWorkItemModel workItem)
        {
            if (actions == null) throw new ArgumentNullException(nameof(actions));
            if (workItem == null) throw new ArgumentNullException(nameof(workItem));

            foreach (var action in actions)
            {
                if (action == null)
                {
                    _logger.LogWarning("⚠️ Null aksiyon tespit edildi, atlanıyor.");
                    continue;
                }

                if (_handlers.TryGetValue(action.ActionName, out var handler))
                {
                    try
                    {
                        await handler.ExecuteAsync(workItem, action, _logger);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"❗ Aksiyon '{action.ActionName}' için hata oluştu. Kural: '{action.ActionName}'.");
                    }
                }
                else
                {
                    _logger.LogWarning($"⚠️ Tanımsız aksiyon tipi: '{action.ActionName}'. Kural: '{action.ActionName}'.");
                }
            }
        }
    }
}
