using BurganAzureDevopsAggregator.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BurganAzureDevopsAggregator.Business
{
    /// <summary>
    /// Rule execution sonuçlarını günlük JSON dosyalarına yazan service
    /// Elasticsearch indexing için optimize edilmiştir
    /// </summary>
    public class RuleExecutionJsonLogger
    {
        private readonly ILogger<RuleExecutionJsonLogger> _logger;
        private readonly string _logDirectory;
        private readonly JsonSerializerOptions _jsonOptions;

        public RuleExecutionJsonLogger(ILogger<RuleExecutionJsonLogger> logger)
        {
            _logger = logger;
            
            // Use /tmp directory - always writable in Docker containers
            _logDirectory = "/tmp";
            
            // JSON serialization options for Elasticsearch
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = false, // Compact JSON for better storage
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            _logger.LogInformation($"📁 JSON rule execution logs will be written to: {_logDirectory}");
        }

        /// <summary>
        /// Rule execution session'ını JSON dosyasına yazar
        /// Dosya adı: rule-executions-YYYY-MM-DD.jsonl (JSON Lines format)
        /// </summary>
        public async Task LogExecutionSessionAsync(RuleExecutionSession session)
        {
            try
            {
                var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
                var fileName = $"rule-executions-{today}.jsonl";
                var filePath = Path.Combine(_logDirectory, fileName);

                // Session document for Elasticsearch
                var sessionDocument = new
                {
                    // Elasticsearch metadata
                    @timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    logType = "rule-execution-session",
                    version = "1.0",
                    
                    // Session information
                    sessionId = session.SessionId,
                    workItemId = session.WorkItemId,
                    workItemType = session.WorkItemType,
                    projectName = session.ProjectName,
                    
                    // Execution summary
                    executionSummary = new
                    {
                        totalRules = session.TotalRules,
                        executedRules = session.ExecutedRules,
                        passedRules = session.PassedRules,
                        failedRules = session.FailedRules,
                        skippedRules = session.SkippedRules,
                        executionDurationMs = session.ExecutionDurationMs,
                        startTime = session.StartTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        endTime = session.EndTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                    },
                    
                    // Rule execution details
                    ruleExecutions = session.RuleExecutions?.Select(re => new
                    {
                        ruleName = re.RuleName,
                        ruleSet = re.RuleSet,
                        appliesTo = re.AppliesTo,
                        priority = re.Priority,
                        expression = re.Expression,
                        status = re.Status, // PASSED, FAILED, SKIPPED
                        executionTimeMs = re.ExecutionTimeMs,
                        errorMessage = re.ErrorMessage,
                        
                        // Actions executed
                        actions = re.Actions?.Select(a => new
                        {
                            actionName = a.ActionName,
                            conditionType = a.ConditionType,
                            executionOrder = a.ExecutionOrder,
                            status = a.Status, // SUCCESS, FAILED
                            errorMessage = a.ErrorMessage,
                            parameters = a.Parameters
                        }).ToList(),
                        
                        // Field changes made
                        fieldChanges = re.FieldChanges?.Select(fc => new
                        {
                            fieldName = fc.FieldName,
                            oldValue = fc.OldValue,
                            newValue = fc.NewValue,
                            changeType = fc.ChangeType // SET, UPDATE, APPEND
                        }).ToList()
                    }).ToList(),
                    
                    // Environment information
                    environment = new
                    {
                        serverName = Environment.MachineName,
                        applicationVersion = "1.0.0", // You can get this from assembly
                        dotnetVersion = Environment.Version.ToString()
                    }
                };

                var jsonLine = JsonSerializer.Serialize(sessionDocument, _jsonOptions);
                
                // Append to daily file (JSON Lines format)
                await File.AppendAllTextAsync(filePath, jsonLine + Environment.NewLine);
                
                // Also write simple rule summary
                await LogRuleSummaryAsync(session);
                
                _logger.LogInformation($"📁 Rule execution session logged to: {fileName} (Directory: {_logDirectory})");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to write rule execution session to JSON file");
            }
        }

        /// <summary>
        /// Simple rule summary log (JSON format: rulename, status, errormessage, timestamp)
        /// </summary>
        private async Task LogRuleSummaryAsync(RuleExecutionSession session)
        {
            try
            {
                var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
                var summaryFileName = $"rule-summary-{today}.jsonl";
                var summaryFilePath = Path.Combine(_logDirectory, summaryFileName);

                // Write each rule result as individual JSON lines
                foreach (var rule in session.RuleExecutions)
                {
                    var ruleSummary = new
                    {
                        rulename = rule.RuleName,
                        status = rule.Status,
                        errormessage = rule.ErrorMessage ?? "",
                        timestamp = session.StartTime.ToString("yyyy-MM-dd HH:mm:ss")
                    };

                    var jsonLine = JsonSerializer.Serialize(ruleSummary, _jsonOptions);
                    await File.AppendAllTextAsync(summaryFilePath, jsonLine + Environment.NewLine);
                }

                _logger.LogInformation($"📋 Rule summary logged to: {summaryFileName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to write rule summary to JSON file");
            }
        }

        /// <summary>
        /// Individual rule execution result'ını JSON dosyasına yazar
        /// Real-time logging için kullanılır
        /// </summary>
        public async Task LogRuleExecutionAsync(RuleExecutionResult result)
        {
            try
            {
                var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
                var fileName = $"rule-executions-{today}.jsonl";
                var filePath = Path.Combine(_logDirectory, fileName);

                var ruleDocument = new
                {
                    @timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    logType = "individual-rule-execution",
                    version = "1.0",
                    
                    sessionId = result.SessionId,
                    workItemId = result.WorkItemId,
                    workItemType = result.WorkItemType,
                    
                    ruleName = result.RuleName,
                    ruleSet = result.RuleSet,
                    appliesTo = result.AppliesTo,
                    priority = result.Priority,
                    expression = result.Expression,
                    status = result.Status,
                    executionTimeMs = result.ExecutionTimeMs,
                    errorMessage = result.ErrorMessage,
                    
                    actions = result.Actions,
                    fieldChanges = result.FieldChanges
                };

                var jsonLine = JsonSerializer.Serialize(ruleDocument, _jsonOptions);
                await File.AppendAllTextAsync(filePath, jsonLine + Environment.NewLine);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to write individual rule execution to JSON file");
            }
        }

        /// <summary>
        /// Log dosyalarını temizler (retention policy)
        /// Elasticsearch'e aktarıldıktan sonra çalıştırılabilir
        /// </summary>
        public async Task CleanupOldLogsAsync(int retentionDays = 7)
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);
                var files = Directory.GetFiles(_logDirectory, "rule-executions-*.jsonl");
                
                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.CreationTimeUtc < cutoffDate)
                    {
                        File.Delete(file);
                        _logger.LogInformation($"🗑️ Deleted old log file: {fileInfo.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to cleanup old log files");
            }
        }

        /// <summary>
        /// Bugünkü log dosyasının path'ini döner
        /// Elasticsearch indexing için kullanılabilir
        /// </summary>
        public string GetTodaysLogFilePath()
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var fileName = $"rule-executions-{today}.jsonl";
            return Path.Combine(_logDirectory, fileName);
        }

        /// <summary>
        /// Belirli tarih aralığındaki log dosyalarını döner
        /// </summary>
        public List<string> GetLogFilesInRange(DateTime startDate, DateTime endDate)
        {
            var logFiles = new List<string>();
            
            for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
            {
                var fileName = $"rule-executions-{date:yyyy-MM-dd}.jsonl";
                var filePath = Path.Combine(_logDirectory, fileName);
                
                if (File.Exists(filePath))
                {
                    logFiles.Add(filePath);
                }
            }
            
            return logFiles;
        }
    }

    #region Data Models for JSON Logging

    public class RuleExecutionSession
    {
        public string SessionId { get; set; } = Guid.NewGuid().ToString();
        public int WorkItemId { get; set; }
        public string WorkItemType { get; set; }
        public string ProjectName { get; set; }
        
        public int TotalRules { get; set; }
        public int ExecutedRules { get; set; }
        public int PassedRules { get; set; }
        public int FailedRules { get; set; }
        public int SkippedRules { get; set; }
        
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public long ExecutionDurationMs { get; set; }
        
        public List<RuleExecutionResult> RuleExecutions { get; set; } = new List<RuleExecutionResult>();
    }

    public class RuleExecutionResult
    {
        public string SessionId { get; set; }
        public int WorkItemId { get; set; }
        public string WorkItemType { get; set; }
        
        public string RuleName { get; set; }
        public string RuleSet { get; set; }
        public string AppliesTo { get; set; }
        public int Priority { get; set; }
        public string Expression { get; set; }
        public string Status { get; set; } // PASSED, FAILED, SKIPPED
        public long ExecutionTimeMs { get; set; }
        public string ErrorMessage { get; set; }
        
        public List<ActionExecutionResult> Actions { get; set; } = new List<ActionExecutionResult>();
        public List<FieldChangeResult> FieldChanges { get; set; } = new List<FieldChangeResult>();
    }

    public class ActionExecutionResult
    {
        public string ActionName { get; set; }
        public string ConditionType { get; set; }
        public int ExecutionOrder { get; set; }
        public string Status { get; set; } // SUCCESS, FAILED
        public string ErrorMessage { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
    }

    public class FieldChangeResult
    {
        public string FieldName { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string ChangeType { get; set; } // SET, UPDATE, APPEND
    }

    #endregion
} 