using BurganAzureDevopsAggregator.Database;
using BurganAzureDevopsAggregator.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq;

public class RulesService
{
    private readonly ApplicationDbContext _context;

    public RulesService(ApplicationDbContext context)
    {
        _context = context;
    }

    // Aktif kuralları (RuleSet bazında) ilişkili aksiyonlar ve parametreleri ile beraber çekmek için:
    public async Task<List<RuleModel>> GetActiveRulesWithActionsAsync(string ruleset)
    {
        return await _context.Rules
            .Where(r => r.IsActive && r.RuleSet == ruleset)
            .OrderBy(r => r.Priority) // Priority'ye göre sıralama eklendi (düşük değer = yüksek öncelik)
            .Include(r => r.Actions)
                .ThenInclude(a => a.Parameters)
            .ToListAsync();
    }

    /// <summary>
    /// Rule name ve RuleSet'e göre mevcut rule'ı bulur (Update için)
    /// </summary>
    public async Task<RuleModel?> FindExistingRuleAsync(string ruleName, string ruleSet)
    {
        return await _context.Rules
            .Include(r => r.Actions)
                .ThenInclude(a => a.Parameters)
            .FirstOrDefaultAsync(r => 
                r.RuleName == ruleName && 
                r.RuleSet == ruleSet);
    }

 public async Task SaveRulesAsync(List<RuleModel> rules)
{
    using var transaction = await _context.Database.BeginTransactionAsync();

    try
    {
        foreach (var rule in rules)
        {
            // EF'e yeni bir entity olduğunu belirt
            rule.RuleId = null;

            if (rule.Actions != null)
            {
                foreach (var action in rule.Actions)
                {
                    action.ActionId = null; // IDENTITY alan
                    action.Rule = rule;     // navigation property

                    if (action.Parameters != null)
                    {
                        foreach (var parameter in action.Parameters)
                        {
                            parameter.ParameterId = null; // IDENTITY alan
                            parameter.Action = action;    // navigation property
                        }
                    }
                }
            }

            // En son rule'ı EF'e ekle (ilişkilerle birlikte)
            _context.Rules.Add(rule);
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}

    /// <summary>
    /// Mevcut rule'ı günceller (Upsert için)
    /// </summary>
    public async Task UpdateRuleAsync(RuleModel existingRule, RuleModel newRule)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // Rule temel bilgilerini güncelle
            existingRule.Expression = newRule.Expression;
            existingRule.AppliesTo = newRule.AppliesTo;
            existingRule.IsActive = newRule.IsActive;
            existingRule.Priority = newRule.Priority;

            // Mevcut action'ları sil
            if (existingRule.Actions?.Any() == true)
            {
                foreach (var action in existingRule.Actions.ToList())
                {
                    if (action.Parameters?.Any() == true)
                    {
                        _context.RuleActionParameters.RemoveRange(action.Parameters);
                    }
                    _context.RuleActions.Remove(action);
                }
            }

            // Yeni action'ları ekle
            if (newRule.Actions?.Any() == true)
            {
                existingRule.Actions = new List<RuleAction>();
                
                foreach (var newAction in newRule.Actions)
                {
                    var action = new RuleAction
                    {
                        ActionName = newAction.ActionName,
                        ConditionType = newAction.ConditionType,
                        Rule = existingRule,
                        Parameters = new List<RuleActionParameter>()
                    };

                    if (newAction.Parameters?.Any() == true)
                    {
                        foreach (var newParam in newAction.Parameters)
                        {
                            action.Parameters.Add(new RuleActionParameter
                            {
                                ParamKey = newParam.ParamKey,
                                ParamValue = newParam.ParamValue,
                                Action = action
                            });
                        }
                    }

                    existingRule.Actions.Add(action);
                }
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Manuel review bekleyen rule'ları getirir
    /// </summary>
    public async Task<List<RuleModel>> GetManualReviewRulesAsync()
    {
        return await _context.Rules
            .Include(r => r.Actions)
                .ThenInclude(a => a.Parameters)
            .Where(r => r.RuleName.Contains("_ManualReview") || !r.IsActive)
            .OrderBy(r => r.Priority)
            .ToListAsync();
    }

    /// <summary>
    /// ID'ye göre rule getirir
    /// </summary>
    public async Task<RuleModel?> GetRuleByIdAsync(int ruleId)
    {
        return await _context.Rules
            .Include(r => r.Actions)
                .ThenInclude(a => a.Parameters)
            .FirstOrDefaultAsync(r => r.RuleId == ruleId);
    }

    /// <summary>
    /// Rule'ı siler
    /// </summary>
    public async Task DeleteRuleAsync(int ruleId)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var rule = await GetRuleByIdAsync(ruleId);
            if (rule != null)
            {
                // İlişkili action'ları ve parameter'ları sil
                if (rule.Actions?.Any() == true)
                {
                    foreach (var action in rule.Actions.ToList())
                    {
                        if (action.Parameters?.Any() == true)
                        {
                            _context.RuleActionParameters.RemoveRange(action.Parameters);
                        }
                        _context.RuleActions.Remove(action);
                    }
                }

                _context.Rules.Remove(rule);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Dashboard için tüm rule'ları statistiklerle birlikte getirir
    /// </summary>
    public async Task<List<RuleModel>> GetAllRulesWithStatsAsync()
    {
        return await _context.Rules
            .Include(r => r.Actions)
                .ThenInclude(a => a.Parameters)
            .OrderBy(r => r.Priority)
            .ToListAsync();
    }
}
