using BurganAzureDevopsAggregator.Models;
using BurganAzureDevopsAggregator.Business;
using RulesEngine.Models;
using Microsoft.Extensions.Logging;
using BurganAzureDevopsAggregator.Actions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.IO;
using Elastic.Apm.Api;
using Elastic.Apm;

public class RulesEngineService
{
    private readonly RulesService _rulesService;
    private readonly ILogger<RulesEngineService> _logger;
    private readonly ActionExecutor _actionExecutor;
    private readonly XmlRuleProcessor _xmlRuleProcessor;
    private readonly RuleExecutionJsonLogger _jsonLogger;
    
    // In-memory circuit breaker için
    private static readonly Dictionary<string, DateTime> _lastExecutionTimes = new();
    private static readonly Dictionary<string, int> _executionCounts = new();
    private static readonly Dictionary<string, string> _lastFieldHashes = new();
    private static DateTime _lastCleanupTime = DateTime.UtcNow;
    
    // Circuit breaker configuration
    private readonly TimeSpan MinExecutionInterval = TimeSpan.FromSeconds(5); // 5 saniye minimum interval
    private readonly int MaxExecutionsPerHour = 20; // Saatte max 20 execution
    private readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5); // 5 dakikada bir cleanup
    private readonly int MaxDictionarySize = 500; // Maximum entries per dictionary

    public RulesEngineService(
        RulesService rulesService, 
        ActionExecutor actionExecutor, 
        XmlRuleProcessor xmlRuleProcessor,
        RuleExecutionJsonLogger jsonLogger,
        ILogger<RulesEngineService> logger)
    {
        _rulesService = rulesService;
        _actionExecutor = actionExecutor;
        _xmlRuleProcessor = xmlRuleProcessor;
        _jsonLogger = jsonLogger;
        _logger = logger;
    }

    public async Task ExecuteRules(List<RuleParameter> ruleParameters, FlatWorkItemModel workItemModel)
    {
        var sessionStartTime = DateTime.UtcNow;
        var sessionId = Guid.NewGuid().ToString();
        
        var fields = (IDictionary<string, object>)workItemModel.Fields;

        var projectName = fields.ContainsKey("System_TeamProject")
            ? fields["System_TeamProject"]?.ToString()
            : string.Empty;

        var workItemType = fields.ContainsKey("System_WorkItemType")
            ? fields["System_WorkItemType"]?.ToString()
            : string.Empty;

        var workflowName = "CommonWorkflow";

        var dbRules = await _rulesService.GetActiveRulesWithActionsAsync(projectName);
        
        // Initialize session tracking
        var session = new RuleExecutionSession
        {
            SessionId = sessionId,
            WorkItemId = workItemModel.WorkItemId,
            WorkItemType = workItemType,
            ProjectName = projectName,
            StartTime = sessionStartTime,
            TotalRules = dbRules?.Count ?? 0
        };

        if (dbRules == null || !dbRules.Any())
        {
            _logger.LogWarning($"⚠️ No active rules found for workflow: {workflowName}");
            return;
        }

        // Rule'ları tip'ine göre ayır
        var normalRules = dbRules.Where(r => !IsXmlCalculationRule(r.Expression)).ToList();
        var xmlRules = dbRules.Where(r => IsXmlCalculationRule(r.Expression)).ToList();

        _logger.LogInformation($"📋 Loading {dbRules.Count} rules in priority order:");
        _logger.LogInformation($"   - Normal Rules: {normalRules.Count}");
        _logger.LogInformation($"   - XML Calculation Rules: {xmlRules.Count}");
        
        // Log all rules with their JSON representation
        LogAllRulesAsJson(dbRules, workItemModel);

        // Normal rule'ları RulesEngine ile çalıştır
        if (normalRules.Any())
        {
            await ExecuteNormalRules(normalRules, ruleParameters, workItemModel, workflowName, session);
        }

        // XML calculation rule'ları ayrı processor ile çalıştır
        if (xmlRules.Any())
        {
            await ExecuteXmlCalculationRules(xmlRules, workItemModel, session);
        }

        // Session'ı sonlandır ve JSON'a yaz
        var sessionEndTime = DateTime.UtcNow;
        session.EndTime = sessionEndTime;
        session.ExecutionDurationMs = (long)(sessionEndTime - sessionStartTime).TotalMilliseconds;
        session.ExecutedRules = session.RuleExecutions.Count;
        session.PassedRules = session.RuleExecutions.Count(r => r.Status == "PASSED");
        session.FailedRules = session.RuleExecutions.Count(r => r.Status == "FAILED");
        session.SkippedRules = session.RuleExecutions.Count(r => r.Status == "SKIPPED");

        // Tek comprehensive execution summary log
        await LogComprehensiveExecutionSummary(session, workItemModel);
        
        // Add APM custom metrics
        try
        {
            Agent.Tracer.CurrentTransaction?.SetLabel("workItemId", workItemModel.WorkItemId.ToString());
            Agent.Tracer.CurrentTransaction?.SetLabel("workItemType", workItemType);
            Agent.Tracer.CurrentTransaction?.SetLabel("projectName", projectName);
            Agent.Tracer.CurrentTransaction?.SetLabel("totalRules", session.ExecutedRules.ToString());
            Agent.Tracer.CurrentTransaction?.SetLabel("passedRules", session.PassedRules.ToString());
            Agent.Tracer.CurrentTransaction?.SetLabel("failedRules", session.FailedRules.ToString());
            Agent.Tracer.CurrentTransaction?.SetLabel("successRate", (session.ExecutedRules > 0 ? (double)session.PassedRules / session.ExecutedRules * 100 : 0).ToString("F1"));
            
            // Circuit breaker memory metrics
            var memoryUsage = _lastExecutionTimes.Count + _executionCounts.Count + _lastFieldHashes.Count;
            Agent.Tracer.CurrentTransaction?.SetLabel("circuitBreakerMemoryUsage", memoryUsage.ToString());
        }
        catch (Exception apmEx)
        {
            _logger.LogWarning(apmEx, "APM labeling failed - continuing execution");
        }
        
        // Log circuit breaker memory status periodically
        LogCircuitBreakerMemoryStatus();
    }

    /// <summary>
    /// Normal boolean rule'ları RulesEngine ile execute eder
    /// </summary>
    private async Task ExecuteNormalRules(List<RuleModel> normalRules, List<RuleParameter> ruleParameters, FlatWorkItemModel workItemModel, string workflowName, RuleExecutionSession session)
    {
        // AppliesTo kontrolü ile filtreleme
        var workItemType = workItemModel.Fields.ContainsKey("System_WorkItemType") 
            ? workItemModel.Fields["System_WorkItemType"]?.ToString() 
            : string.Empty;

        var filteredRules = normalRules.Where(rule =>
        {
            var appliesTo = GetAppliesTo(rule);
            
            // Multiple work item types support (comma separated)
            if (!string.IsNullOrEmpty(appliesTo) && !string.Equals(appliesTo, "All", StringComparison.OrdinalIgnoreCase))
            {
                var appliesToTypes = appliesTo.Split(',').Select(t => t.Trim());
                var matches = appliesToTypes.Any(t => string.Equals(t, workItemType, StringComparison.OrdinalIgnoreCase));
                
                if (!matches)
                {
                    _logger.LogDebug($"⏭️ Skipping rule '{rule.RuleName}' - doesn't apply to '{workItemType}' (AppliesTo: {appliesTo})");
                    return false;
                }
            }
            
            return true;
        }).ToList();

        if (!filteredRules.Any())
        {
            _logger.LogInformation($"📋 No normal rules apply to WorkItem type '{workItemType}'");
            return;
        }

        // Circuit breaker ile filtrele - sonsuz döngüyü önle
        var originalCount = filteredRules.Count;
        
        // Memory kontrolü ve proaktif cleanup
        var totalMemoryUsage = _lastExecutionTimes.Count + _executionCounts.Count + _lastFieldHashes.Count;
        if (totalMemoryUsage > MaxDictionarySize * 2) // Threshold: 1000 entries
        {
            _logger.LogWarning($"⚠️ Circuit breaker memory usage critical: {totalMemoryUsage} entries. Forcing immediate cleanup.");
            CleanupOldCounters(DateTime.UtcNow);
        }
        
        filteredRules = FilterRulesByCircuitBreaker(filteredRules, workItemModel);
        var blockedCount = originalCount - filteredRules.Count;
        
        if (blockedCount > 0)
        {
            _logger.LogWarning($"🛑 Circuit breaker blocked {blockedCount} of {originalCount} rules");
        }
        
        if (!filteredRules.Any())
        {
            _logger.LogWarning($"🛑 All rules blocked by circuit breaker for WorkItem {workItemModel.WorkItemId}");
            return;
        }

        _logger.LogInformation($"📋 Executing {filteredRules.Count}/{normalRules.Count} normal rules for WorkItem type '{workItemType}'");

        var workflow = new Workflow
        {
            WorkflowName = workflowName,
            Rules = filteredRules.Select(r => new Rule
            {
                RuleName = r.RuleName,
                Expression = r.Expression,
                Actions = null
            }).ToList()
        };

        var rulesEngine = new RulesEngine.RulesEngine(new[] { workflow });

        try
        {
            var results = await rulesEngine.ExecuteAllRulesAsync(workflowName, ruleParameters.ToArray());

            foreach (var result in results)
            {
                var ruleName = result.Rule.RuleName;
                var expression = result.Rule.Expression;
                var ruleModel = filteredRules.FirstOrDefault(r => r.RuleName == ruleName);

                if (result.IsSuccess)
                {
                    _logger.LogInformation($"✅ RuleSet: '{workflow.WorkflowName}' - Kural: '{ruleName}' geçti. Expression: {expression}");
                    
                    // Circuit breaker counter'ını güncelle
                    UpdateCircuitBreakerCounters(ruleName, workItemModel.WorkItemId, workItemModel);
                    
                    // Track rule execution for JSON logging
                    var ruleExecution = new RuleExecutionResult
                    {
                        SessionId = session.SessionId,
                        WorkItemId = workItemModel.WorkItemId,
                        WorkItemType = session.WorkItemType,
                        RuleName = ruleName,
                        RuleSet = ruleModel?.RuleSet,
                        AppliesTo = ruleModel?.AppliesTo,
                        Priority = ruleModel?.Priority ?? 0,
                        Expression = expression,
                        Status = "PASSED",
                        ExecutionTimeMs = 0, // You can add timing if needed
                        ErrorMessage = result.ExceptionMessage
                    };
                    
                    session.RuleExecutions.Add(ruleExecution);
                    
                    // Rule logged in comprehensive summary

                    if (ruleModel?.Actions != null)
                    {
                        var successActions = ruleModel.Actions
                            .Where(a => string.Equals(a.ConditionType, "Success", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (successActions.Any())
                        {
                            try
                            {
                                await _actionExecutor.ExecuteActionsAsync(successActions, workItemModel);
                                foreach (var action in successActions)
                                {
                                    _logger.LogInformation($"✅ Action '{action.ActionName}' executed successfully for rule '{ruleName}'.");
                                }
                            }
                            catch (Exception actionEx)
                            {
                                _logger.LogError(actionEx, $"❗ Error executing actions for rule '{ruleName}'.");
                            }
                        }
                    }
                }
                else
                {
                    _logger.LogWarning($"❌ RuleSet: '{workflow.WorkflowName}' - Kural: '{ruleName}' geçemedi. Expression: {expression}");
                    
                    // Track failed rule execution for JSON logging
                    var ruleExecution = new RuleExecutionResult
                    {
                        SessionId = session.SessionId,
                        WorkItemId = workItemModel.WorkItemId,
                        WorkItemType = session.WorkItemType,
                        RuleName = ruleName,
                        RuleSet = ruleModel?.RuleSet,
                        AppliesTo = ruleModel?.AppliesTo,
                        Priority = ruleModel?.Priority ?? 0,
                        Expression = expression,
                        Status = "FAILED",
                        ExecutionTimeMs = 0,
                        ErrorMessage = result.ExceptionMessage
                    };
                    
                    session.RuleExecutions.Add(ruleExecution);
                    
                    // Rule logged in comprehensive summary

                    if (ruleModel?.Actions != null)
                    {
                        var failureActions = ruleModel.Actions
                            .Where(a => string.Equals(a.ConditionType, "Failure", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (failureActions.Any())
                        {
                            try
                            {
                                await _actionExecutor.ExecuteActionsAsync(failureActions, workItemModel);
                                foreach (var action in failureActions)
                                {
                                    _logger.LogInformation($"⚠️ Failure Action '{action.ActionName}' executed for failed rule '{ruleName}'.");
                                }
                            }
                            catch (Exception actionEx)
                            {
                                _logger.LogError(actionEx, $"❗ Error executing failure actions for rule '{ruleName}'.");
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"❗ Error executing normal rules for workflow '{workflow.WorkflowName}'. Exception details: {ex}");
        }
    }

    /// <summary>
    /// XML calculation rule'ları execute eder
    /// </summary>
    private async Task ExecuteXmlCalculationRules(List<RuleModel> xmlRules, FlatWorkItemModel workItemModel, RuleExecutionSession session)
    {
        foreach (var rule in xmlRules)
        {
            try
            {
                // AppliesTo kontrolü
                var workItemType = workItemModel.Fields.ContainsKey("System_WorkItemType") 
                    ? workItemModel.Fields["System_WorkItemType"]?.ToString() 
                    : string.Empty;

                var appliesTo = GetAppliesTo(rule);
                if (!string.IsNullOrEmpty(appliesTo) && !string.Equals(appliesTo, "All", StringComparison.OrdinalIgnoreCase))
                {
                    // Multiple work item types support (comma separated)
                    var appliesToTypes = appliesTo.Split(',').Select(t => t.Trim());
                    var matches = appliesToTypes.Any(t => string.Equals(t, workItemType, StringComparison.OrdinalIgnoreCase));
                    
                    if (!matches)
                    {
                        _logger.LogDebug($"⏭️ Skipping XML rule '{rule.RuleName}' - doesn't apply to '{workItemType}' (AppliesTo: {appliesTo})");
                        continue;
                    }
                }

                // XML calculation rule'ı execute et
                var xmlRule = ExtractXmlRuleFromDatabase(rule);
                if (xmlRule != null)
                {
                    var success = await _xmlRuleProcessor.ExecuteXmlCalculationRuleAsync(xmlRule, workItemModel);
                    
                    if (success)
                    {
                        _logger.LogInformation($"✅ XML Calculation Rule '{rule.RuleName}' executed successfully");
                        // Rule logged in comprehensive summary
                    }
                    else
                    {
                        _logger.LogWarning($"⚠️ XML Calculation Rule '{rule.RuleName}' execution failed");
                        // Rule logged in comprehensive summary
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❗ Error executing XML rule '{rule.RuleName}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Rule'ın XML calculation rule olup olmadığını kontrol eder
    /// </summary>
    private bool IsXmlCalculationRule(string expression)
    {
        return !string.IsNullOrEmpty(expression) && expression.StartsWith("XmlCalculationRule(", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Database rule'ından XmlCalculationRule extract eder
    /// </summary>
    private XmlCalculationRule ExtractXmlRuleFromDatabase(RuleModel rule)
    {
        try
        {
            var action = rule.Actions?.FirstOrDefault(a => a.ActionName == "ExecuteXmlCalculation");
            if (action?.Parameters == null) return null;

            var xmlRuleName = action.Parameters.FirstOrDefault(p => p.ParamKey == "XmlRuleName")?.ParamValue;
            var csharpCode = action.Parameters.FirstOrDefault(p => p.ParamKey == "CSharpCode")?.ParamValue;
            // AppliesTo artık parameter'da değil, rule.AppliesTo'da

            if (string.IsNullOrEmpty(xmlRuleName) || string.IsNullOrEmpty(csharpCode))
                return null;

            return new XmlCalculationRule
            {
                Name = xmlRuleName,
                CSharpCode = csharpCode,
                AppliesTo = rule.AppliesTo ?? "All" // RuleModel.AppliesTo'dan al
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to extract XML rule from database rule '{rule.RuleName}'");
            return null;
        }
    }

    /// <summary>
    /// Rule'ın appliesTo değerini alır
    /// </summary>
    private string GetAppliesTo(RuleModel rule)
    {
        // AppliesTo artık sadece RuleModel'de var, parameter'da yok
        return rule.AppliesTo ?? "All";
    }

    /// <summary>
    /// Tüm rule'ları JSON formatında loglar
    /// </summary>
    private void LogAllRulesAsJson(List<RuleModel> rules, FlatWorkItemModel workItemModel)
    {
        try
        {
            _logger.LogInformation("📋 ========== RULE EXECUTION SESSION START ==========");
            _logger.LogInformation($"🎯 WorkItem ID: {workItemModel.WorkItemId}");
            _logger.LogInformation($"🏷️ WorkItem Type: {workItemModel.Fields.GetValueOrDefault("System_WorkItemType", "Unknown")}");
            _logger.LogInformation($"📊 Total Rules to Evaluate: {rules.Count}");

            foreach (var rule in rules.OrderBy(r => r.Priority))
            {
                var ruleJson = JsonSerializer.Serialize(new
                {
                    RuleName = rule.RuleName,
                    Expression = rule.Expression,
                    AppliesTo = rule.AppliesTo,
                    RuleSet = rule.RuleSet,
                    IsActive = rule.IsActive,
                    Priority = rule.Priority,
                    Actions = rule.Actions?.Select(a => new
                    {
                        ActionName = a.ActionName,
                        ConditionType = a.ConditionType,
                        ExecutionOrder = a.ExecutionOrder,
                        Parameters = a.Parameters?.Select(p => new { p.ParamKey, p.ParamValue }).ToList()
                    }).ToList()
                }, new JsonSerializerOptions { WriteIndented = true });

                _logger.LogInformation($"📄 Rule Definition [{rule.Priority}]: {rule.RuleName}");
                _logger.LogInformation($"📄 JSON: {ruleJson}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error logging rules as JSON");
        }
    }



    /// <summary>
    /// Comprehensive execution summary with all rule statuses and errors
    /// </summary>
    private async Task LogComprehensiveExecutionSummary(RuleExecutionSession session, FlatWorkItemModel workItemModel)
    {
        try
        {
            var executionSummary = new
            {
                @timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                logType = "execution-summary",
                sessionId = session.SessionId,
                workItemId = workItemModel.WorkItemId,
                workItemType = session.WorkItemType,
                projectName = session.ProjectName,
                
                // Flattened summary fields
                totalRules = session.ExecutedRules,
                passedRules = session.PassedRules,
                failedRules = session.FailedRules,
                skippedRules = session.SkippedRules,
                executionDurationMs = session.ExecutionDurationMs,
                successRate = session.ExecutedRules > 0 ? Math.Round((double)session.PassedRules / session.ExecutedRules * 100, 1) : 0,
                executionTime = session.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                
                // Failed rules only (for quick error identification)
                failedRuleNames = string.Join(", ", session.RuleExecutions.Where(r => r.Status == "FAILED").Select(r => r.RuleName)),
                failedRuleErrors = string.Join(" | ", session.RuleExecutions.Where(r => r.Status == "FAILED").Select(r => $"{r.RuleName}: {r.ErrorMessage}").Where(s => !string.IsNullOrEmpty(s.Split(':')[1].Trim())))
            };

            var summaryJson = JsonSerializer.Serialize(executionSummary, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            _logger.LogInformation("🎯 ========== COMPREHENSIVE EXECUTION SUMMARY ==========");
            _logger.LogInformation($"📊 WorkItem {workItemModel.WorkItemId}: {session.PassedRules}/{session.ExecutedRules} rules passed ({(session.ExecutedRules > 0 ? (double)session.PassedRules / session.ExecutedRules * 100 : 0):F1}%)");
            
            // Log failed rules prominently
            if (session.FailedRules > 0)
            {
                var failedRules = session.RuleExecutions.Where(r => r.Status == "FAILED").ToList();
                _logger.LogWarning($"❌ FAILED RULES ({failedRules.Count}):");
                foreach (var failed in failedRules)
                {
                    _logger.LogWarning($"   🔸 {failed.RuleName}: {failed.ErrorMessage}");
                }
            }
            
            _logger.LogInformation($"📋 Detailed Summary JSON:\n{summaryJson}");
            _logger.LogInformation("=======================================================");
            
            // JSON dosyasına da yaz
            await WriteToJsonFile(executionSummary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error logging comprehensive execution summary");
        }
    }

    /// <summary>
    /// JSON dosyasına execution summary yazır
    /// </summary>
    private async Task WriteToJsonFile(object executionSummary)
    {
        try
        {
            var logDirectory = "/tmp";
            Directory.CreateDirectory(logDirectory);
            
            var fileName = $"rule-executions-{DateTime.UtcNow:yyyy-MM-dd}.jsonl";
            var filePath = Path.Combine(logDirectory, fileName);
            
            var jsonLine = JsonSerializer.Serialize(executionSummary, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            
            await File.AppendAllTextAsync(filePath, jsonLine + Environment.NewLine);
            
            _logger.LogInformation($"📄 JSON log written to: {filePath}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error writing JSON to file");
        }
    }

    /// <summary>
    /// Circuit breaker ile rule'ları filtreler - sonsuz döngüyü önler
    /// </summary>
    private List<RuleModel> FilterRulesByCircuitBreaker(List<RuleModel> rules, FlatWorkItemModel workItemModel)
    {
        var filteredRules = new List<RuleModel>();
        var now = DateTime.UtcNow;
        
        // Cleanup eski kayıtları
        CleanupOldCounters(now);
        
        foreach (var rule in rules)
        {
            var ruleKey = $"Rule_{rule.RuleName}_{workItemModel.WorkItemId}";
            var fieldsHash = GenerateFieldsHash(workItemModel.Fields);
            
            // Time-based check
            if (_lastExecutionTimes.ContainsKey(ruleKey))
            {
                var timeSinceLastExecution = now - _lastExecutionTimes[ruleKey];
                if (timeSinceLastExecution < MinExecutionInterval)
                {
                    _logger.LogWarning($"🛑 Circuit breaker: Rule {rule.RuleName} blocked - executed {timeSinceLastExecution.TotalSeconds:F1}s ago (min: {MinExecutionInterval.TotalSeconds}s)");
                    continue;
                }
            }
            
            // Count-based check
            var countKey = $"Count_{rule.RuleName}_{workItemModel.WorkItemId}_{now:yyyy-MM-dd-HH}";
            if (_executionCounts.ContainsKey(countKey) && _executionCounts[countKey] >= MaxExecutionsPerHour)
            {
                _logger.LogWarning($"🛑 Circuit breaker: Rule {rule.RuleName} blocked - hourly limit reached ({MaxExecutionsPerHour})");
                continue;
            }
            
            // Field-based check (aynı field değerleri ile çok yakın zamanda çalıştırılmış mı?)
            var fieldKey = $"Fields_{rule.RuleName}_{workItemModel.WorkItemId}";
            if (_lastFieldHashes.ContainsKey(fieldKey) && _lastFieldHashes[fieldKey] == fieldsHash)
            {
                if (_lastExecutionTimes.ContainsKey(ruleKey))
                {
                    var fieldTimeDiff = now - _lastExecutionTimes[ruleKey];
                    if (fieldTimeDiff < TimeSpan.FromSeconds(10)) // 10 saniye içinde aynı field'lar ile
                    {
                        _logger.LogWarning($"🛑 Circuit breaker: Rule {rule.RuleName} blocked - same field values executed {fieldTimeDiff.TotalSeconds:F1}s ago");
                        continue;
                    }
                }
            }
            
            filteredRules.Add(rule);
        }
        
        return filteredRules;
    }
    
    /// <summary>
    /// Circuit breaker counter'larını günceller
    /// </summary>
    private void UpdateCircuitBreakerCounters(string ruleName, int workItemId, FlatWorkItemModel workItemModel)
    {
        var now = DateTime.UtcNow;
        var ruleKey = $"Rule_{ruleName}_{workItemId}";
        var countKey = $"Count_{ruleName}_{workItemId}_{now:yyyy-MM-dd-HH}";
        var fieldKey = $"Fields_{ruleName}_{workItemId}";
        var fieldsHash = GenerateFieldsHash(workItemModel.Fields);
        
        // Update execution time
        _lastExecutionTimes[ruleKey] = now;
        
        // Update execution count
        _executionCounts[countKey] = _executionCounts.GetValueOrDefault(countKey, 0) + 1;
        
        // Update field hash
        _lastFieldHashes[fieldKey] = fieldsHash;
        
        var totalEntries = _lastExecutionTimes.Count + _executionCounts.Count + _lastFieldHashes.Count;
        _logger.LogDebug($"📊 Circuit breaker updated: {ruleName} - Count: {_executionCounts[countKey]}, Total entries: {totalEntries}");
        
        // Proaktif cleanup eğer memory kullanımı yüksekse
        if (totalEntries > MaxDictionarySize * 3) // 1500 entry threshold
        {
            _logger.LogWarning($"⚠️ Circuit breaker memory critical during update: {totalEntries} entries. Triggering emergency cleanup.");
            Task.Run(() => CleanupOldCounters(now)); // Async cleanup to avoid blocking
        }
    }
    
    /// <summary>
    /// Eski counter'ları temizler - memory leak'i önler
    /// </summary>
    private void CleanupOldCounters(DateTime now)
    {
        if (now - _lastCleanupTime < CleanupInterval)
            return;
            
        _logger.LogDebug($"🧹 Starting circuit breaker cleanup. Current entries: Times={_lastExecutionTimes.Count}, Counts={_executionCounts.Count}, Fields={_lastFieldHashes.Count}");
        
        var cutoffTime = now.AddMinutes(-30); // 30 dakika öncesini sil (daha agresif)
        var initialCounts = new { Times = _lastExecutionTimes.Count, Counts = _executionCounts.Count, Fields = _lastFieldHashes.Count };
        
        // 1. Eski execution time'ları temizle
        var oldTimeKeys = _lastExecutionTimes.Where(kvp => kvp.Value < cutoffTime).Select(kvp => kvp.Key).ToList();
        foreach (var key in oldTimeKeys)
        {
            _lastExecutionTimes.Remove(key);
        }
        
        // 2. Eski count'ları temizle (eski saat dilimlerine ait) - daha agresif cleanup
        var currentHour = now.ToString("yyyy-MM-dd-HH");
        var previousHour = now.AddHours(-1).ToString("yyyy-MM-dd-HH");
        var oldCountKeys = _executionCounts.Keys.Where(key => 
        {
            // Sadece mevcut ve önceki saati tut, geri kalanını sil
            return !key.Contains(currentHour) && !key.Contains(previousHour);
        }).ToList();
        
        foreach (var key in oldCountKeys)
        {
            _executionCounts.Remove(key);
        }
        
        // 3. Eski field hash'leri temizle - orphaned field hashes
        var validRuleKeys = _lastExecutionTimes.Keys.Where(k => k.StartsWith("Rule_")).ToHashSet();
        var oldFieldKeys = _lastFieldHashes.Keys.Where(key => 
        {
            var correspondingRuleKey = key.Replace("Fields_", "Rule_");
            return !validRuleKeys.Contains(correspondingRuleKey);
        }).ToList();
        
        foreach (var key in oldFieldKeys)
        {
            _lastFieldHashes.Remove(key);
        }
        
        // 4. Maksimum boyut kontrolü - zorla temizlik
        ForceCleanupIfNeeded(now);
        
        _lastCleanupTime = now;
        
        var finalCounts = new { Times = _lastExecutionTimes.Count, Counts = _executionCounts.Count, Fields = _lastFieldHashes.Count };
        var totalMemoryUsage = finalCounts.Times + finalCounts.Counts + finalCounts.Fields;
        
        _logger.LogInformation($"🧹 Circuit breaker cleanup completed:");
        _logger.LogInformation($"   📊 Before: Times={initialCounts.Times}, Counts={initialCounts.Counts}, Fields={initialCounts.Fields}");
        _logger.LogInformation($"   📊 After:  Times={finalCounts.Times}, Counts={finalCounts.Counts}, Fields={finalCounts.Fields}");
        _logger.LogInformation($"   🗑️ Removed: Times={initialCounts.Times - finalCounts.Times}, Counts={initialCounts.Counts - finalCounts.Counts}, Fields={initialCounts.Fields - finalCounts.Fields}");
        _logger.LogInformation($"   💾 Total memory entries: {totalMemoryUsage}");
        
        if (totalMemoryUsage > 1000)
        {
            _logger.LogWarning($"⚠️ Circuit breaker memory usage still high: {totalMemoryUsage} entries after cleanup");
        }
    }
    
    /// <summary>
    /// Maksimum boyut aşıldığında zorla temizlik yapar
    /// </summary>
    private void ForceCleanupIfNeeded(DateTime now)
    {
        bool forceCleanupNeeded = false;
        
        // Her dictionary için maksimum boyut kontrolü
        if (_lastExecutionTimes.Count > MaxDictionarySize)
        {
            _logger.LogWarning($"⚠️ Execution times dictionary size ({_lastExecutionTimes.Count}) exceeds limit ({MaxDictionarySize}). Forcing cleanup.");
            
            // En eski %50'sini sil
            var sortedByTime = _lastExecutionTimes.OrderBy(kvp => kvp.Value).Take(_lastExecutionTimes.Count / 2).ToList();
            foreach (var kvp in sortedByTime)
            {
                _lastExecutionTimes.Remove(kvp.Key);
            }
            forceCleanupNeeded = true;
        }
        
        if (_executionCounts.Count > MaxDictionarySize)
        {
            _logger.LogWarning($"⚠️ Execution counts dictionary size ({_executionCounts.Count}) exceeds limit ({MaxDictionarySize}). Forcing cleanup.");
            
            // Sadece son 2 saati tut, geri kalanını sil
            var currentHour = now.ToString("yyyy-MM-dd-HH");
            var previousHour = now.AddHours(-1).ToString("yyyy-MM-dd-HH");
            var keysToRemove = _executionCounts.Keys.Where(key => 
                !key.Contains(currentHour) && !key.Contains(previousHour)
            ).ToList();
            
            foreach (var key in keysToRemove)
            {
                _executionCounts.Remove(key);
            }
            forceCleanupNeeded = true;
        }
        
        if (_lastFieldHashes.Count > MaxDictionarySize)
        {
            _logger.LogWarning($"⚠️ Field hashes dictionary size ({_lastFieldHashes.Count}) exceeds limit ({MaxDictionarySize}). Forcing cleanup.");
            
            // En son kullanılan %50'sini tut (execution time'a göre)
            var validEntries = _lastFieldHashes.Where(fieldHash =>
            {
                var correspondingRuleKey = fieldHash.Key.Replace("Fields_", "Rule_");
                return _lastExecutionTimes.ContainsKey(correspondingRuleKey);
            }).OrderByDescending(fieldHash =>
            {
                var correspondingRuleKey = fieldHash.Key.Replace("Fields_", "Rule_");
                return _lastExecutionTimes.GetValueOrDefault(correspondingRuleKey, DateTime.MinValue);
            }).Take(MaxDictionarySize / 2).ToList();
            
            _lastFieldHashes.Clear();
            foreach (var kvp in validEntries)
            {
                _lastFieldHashes[kvp.Key] = kvp.Value;
            }
            forceCleanupNeeded = true;
        }
        
        if (forceCleanupNeeded)
        {
            _logger.LogWarning($"🗑️ Force cleanup completed. New sizes: Times={_lastExecutionTimes.Count}, Counts={_executionCounts.Count}, Fields={_lastFieldHashes.Count}");
            
            // GC'yi çalıştır
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            _logger.LogInformation($"🗑️ Garbage collection triggered after force cleanup");
        }
    }
    
    /// <summary>
    /// Field'ların hash'ini oluşturur - field değişikliklerini detect etmek için
    /// </summary>
    private string GenerateFieldsHash(IDictionary<string, object> fields)
    {
        try
        {
            var relevantFields = new[] { "System_State", "System_AssignedTo", "System_AreaPath", "System_Title" };
            var hashInput = string.Join("|", relevantFields.Select(f => 
                fields.ContainsKey(f) ? $"{f}:{fields[f]}" : $"{f}:null"
            ));
            
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(hashInput));
            return Convert.ToBase64String(hashBytes).Substring(0, 16); // İlk 16 karakter yeterli
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"⚠️ Error generating fields hash: {ex.Message}");
            return DateTime.UtcNow.Ticks.ToString(); // Fallback
        }
    }
    
    /// <summary>
    /// Circuit breaker memory durumunu loglar
    /// </summary>
    private void LogCircuitBreakerMemoryStatus()
    {
        try
        {
            var memoryStats = new
            {
                ExecutionTimes = _lastExecutionTimes.Count,
                ExecutionCounts = _executionCounts.Count,
                FieldHashes = _lastFieldHashes.Count,
                TotalEntries = _lastExecutionTimes.Count + _executionCounts.Count + _lastFieldHashes.Count,
                MaxAllowedPerDictionary = MaxDictionarySize,
                LastCleanup = _lastCleanupTime.ToString("yyyy-MM-dd HH:mm:ss"),
                NextCleanupIn = CleanupInterval - (DateTime.UtcNow - _lastCleanupTime)
            };
            
            var isMemoryHigh = memoryStats.TotalEntries > MaxDictionarySize * 2;
            var logLevel = isMemoryHigh ? "WARNING" : "INFO";
            
            _logger.LogInformation($"💾 [{logLevel}] Circuit Breaker Memory Status:");
            _logger.LogInformation($"   📊 Execution Times: {memoryStats.ExecutionTimes}");
            _logger.LogInformation($"   📊 Execution Counts: {memoryStats.ExecutionCounts}");
            _logger.LogInformation($"   📊 Field Hashes: {memoryStats.FieldHashes}");
            _logger.LogInformation($"   📊 Total Entries: {memoryStats.TotalEntries} / {MaxDictionarySize * 3} (threshold)");
            _logger.LogInformation($"   🕐 Last Cleanup: {memoryStats.LastCleanup}");
            _logger.LogInformation($"   ⏱️ Next Cleanup In: {memoryStats.NextCleanupIn.TotalMinutes:F1} minutes");
            
            if (isMemoryHigh)
            {
                _logger.LogWarning($"⚠️ Circuit breaker memory usage is HIGH! Consider reducing cleanup interval or increasing cleanup frequency.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "❌ Error logging circuit breaker memory status");
        }
    }

}
