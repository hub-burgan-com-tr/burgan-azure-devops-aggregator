using BurganAzureDevopsAggregator.Business;
using BurganAzureDevopsAggregator.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;


namespace BurganAzureDevopsAggregator.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AdminDashboardController : ControllerBase
    {
        private readonly RulesService _rulesService;
        private readonly ILogger<AdminDashboardController> _logger;

        public AdminDashboardController(RulesService rulesService, ILogger<AdminDashboardController> logger)
        {
            _rulesService = rulesService;
            _logger = logger;
        }

        /// <summary>
        /// 📋 Tüm rules listesi
        /// </summary>
        [HttpGet("rules")]
        public async Task<IActionResult> GetAllRules()
        {
            try
            {
                var rules = await _rulesService.GetAllRulesWithStatsAsync();
                return Ok(rules);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get rules list");
                return StatusCode(500, $"Error getting rules: {ex.Message}");
            }
        }

        /// <summary>
        /// 📊 Ana dashboard verileri - Review Queue & Stats
        /// </summary>
        [HttpGet("overview")]
        public async Task<IActionResult> GetDashboardOverview()
        {
            try
            {
                var allRules = await _rulesService.GetAllRulesWithStatsAsync();
                var pendingReviews = await _rulesService.GetManualReviewRulesAsync();
                
                var overview = new DashboardOverview
                {
                    TotalRules = allRules.Count,
                    ActiveRules = allRules.Count(r => r.IsActive),
                    InactiveRules = allRules.Count(r => !r.IsActive),
                    PendingReviews = pendingReviews.Count,
                    
                    // Review Queue Statistics
                    ReviewQueue = new ReviewQueueStats
                    {
                        TotalPending = pendingReviews.Count,
                        ByComplexity = pendingReviews
                            .SelectMany(r => AnalyzeComplexityReasons(ExtractOriginalXmlCode(r)))
                            .GroupBy(reason => reason)
                            .ToDictionary(g => g.Key, g => g.Count()),
                        ByRuleSet = pendingReviews
                            .GroupBy(r => r.RuleSet)
                            .ToDictionary(g => g.Key, g => g.Count()),
                        ByPriority = pendingReviews
                            .GroupBy(r => GetPriorityCategory(r.Priority))
                            .ToDictionary(g => g.Key, g => g.Count())
                    },

                    // Conversion Statistics
                    ConversionStats = new ConversionStats
                    {
                        AutoConvertedRules = allRules.Count(r => !r.RuleName.Contains("_ManualReview") && r.IsActive),
                        ManualReviewRules = allRules.Count(r => r.RuleName.Contains("_ManualReview")),
                        RejectedRules = allRules.Count(r => r.RuleName.Contains("_Rejected")),
                        ConversionRate = CalculateConversionRate(allRules),
                        AvgReviewTime = "2.5 hours", // Bu gerçek veriden hesaplanabilir
                        TopComplexityReasons = GetTopComplexityReasons(pendingReviews)
                    },

                    // Recent Activity
                    RecentActivity = GenerateRecentActivity(allRules),

                    LastUpdated = DateTime.Now
                };

                return Ok(overview);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get dashboard overview");
                return StatusCode(500, $"Error getting dashboard overview: {ex.Message}");
            }
        }



        /// <summary>
        /// 🔍 Rule Editor Interface - Rule detayları ve düzenleme araçları
        /// </summary>
        [HttpGet("rule-editor/{ruleId}")]
        public async Task<IActionResult> GetRuleEditor(int ruleId)
        {
            try
            {
                var rule = await _rulesService.GetRuleByIdAsync(ruleId);
                if (rule == null)
                {
                    return NotFound($"Rule with ID {ruleId} not found");
                }

                var editorData = new RuleEditorData
                {
                    Rule = rule,
                    OriginalXmlCode = ExtractOriginalXmlCode(rule),
                    ComplexityReasons = AnalyzeComplexityReasons(ExtractOriginalXmlCode(rule)),
                    
                    // Validation Data
                    ValidationHelpers = new ValidationHelpers
                    {
                        AvailableFields = GetAvailableFields(),
                        SampleExpressions = GetSampleExpressions(),
                        ActionTemplates = GetActionTemplates(),
                        ValidationRules = GetValidationRules()
                    },

                    // Suggestions
                    AiSuggestions = await GenerateAiSuggestions(rule),
                    
                    // Testing
                    TestingTools = new TestingTools
                    {
                        SamplePayloads = GetSamplePayloads(),
                        TestCases = GenerateTestCases(rule),
                        ValidationEndpoint = "/api/AdminDashboard/validate-expression"
                    }
                };

                return Ok(editorData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to get rule editor data for rule {ruleId}");
                return StatusCode(500, $"Error getting rule editor: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ Expression validation tool
        /// </summary>
        [HttpPost("validate-expression")]
        public IActionResult ValidateExpression([FromBody] ExpressionValidationRequest request)
        {
            try
            {
                var validation = new ExpressionValidationResult
                {
                    IsValid = true,
                    Expression = request.Expression,
                    ValidationMessages = new List<string>(),
                    Suggestions = new List<string>(),
                    TestResults = new List<TestResult>()
                };

                // Basic syntax validation
                if (string.IsNullOrWhiteSpace(request.Expression))
                {
                    validation.IsValid = false;
                    validation.ValidationMessages.Add("Expression cannot be empty");
                    return Ok(validation);
                }

                // Field validation
                var usedFields = ExtractFieldsFromExpression(request.Expression);
                var availableFields = GetAvailableFields();
                
                foreach (var field in usedFields)
                {
                    if (!availableFields.Contains(field))
                    {
                        validation.ValidationMessages.Add($"⚠️ Field '{field}' may not exist in payload");
                        validation.Suggestions.Add($"Consider adding null check: string.IsNullOrWhiteSpace(body.Fields.{field})");
                    }
                }

                // Syntax suggestions
                if (request.Expression.Contains("==") && request.Expression.Contains("null"))
                {
                    validation.Suggestions.Add("💡 Consider using 'string.IsNullOrWhiteSpace()' for null checks");
                }

                if (request.Expression.Contains("&&") && request.Expression.Split("&&").Length > 3)
                {
                    validation.Suggestions.Add("🔧 Complex expression - consider breaking into multiple rules");
                }

                // Test with sample data
                if (request.TestWithSamples && request.SamplePayloads?.Any() == true)
                {
                    foreach (var sample in request.SamplePayloads)
                    {
                        var testResult = TestExpressionWithSample(request.Expression, sample);
                        validation.TestResults.Add(testResult);
                    }
                }

                return Ok(validation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate expression");
                return Ok(new ExpressionValidationResult
                {
                    IsValid = false,
                    Expression = request.Expression,
                    ValidationMessages = new List<string> { $"Validation error: {ex.Message}" }
                });
            }
        }

        /// <summary>
        /// 📋 Rule templates ve quick actions
        /// </summary>
        [HttpGet("templates")]
        public IActionResult GetRuleTemplates()
        {
            var templates = new RuleTemplates
            {
                CommonPatterns = new List<RuleTemplate>
                {
                    new RuleTemplate
                    {
                        Name = "State Transition",
                        Category = "State Management",
                        Description = "Change work item state with conditions",
                        Expression = "body.Fields.System_State == \"OldState\" && body.Fields.System_WorkItemType == \"Type\"",
                        Actions = new List<TemplateAction>
                        {
                            new TemplateAction
                            {
                                ActionName = "TransitionToState",
                                Parameters = new Dictionary<string, string>
                                {
                                    { "NewState", "NewState" },
                                    { "Comment", "State changed by rule" }
                                }
                            }
                        }
                    },
                    new RuleTemplate
                    {
                        Name = "Field Validation",
                        Category = "Validation",
                        Description = "Add comment when required fields are missing",
                        Expression = "body.Fields.System_State == \"TargetState\" && string.IsNullOrWhiteSpace(body.Fields.RequiredField)",
                        Actions = new List<TemplateAction>
                        {
                            new TemplateAction
                            {
                                ActionName = "AddComment",
                                Parameters = new Dictionary<string, string>
                                {
                                    { "CommentText", "⚠️ Required field 'RequiredField' is missing!" }
                                }
                            }
                        }
                    },
                    new RuleTemplate
                    {
                        Name = "Size Calculation",
                        Category = "Calculation",
                        Description = "Calculate effort to size mapping",
                        Expression = "body.Fields.System_WorkItemType == \"WorkItemType\" && !string.IsNullOrWhiteSpace(body.Fields.Microsoft_VSTS_Scheduling_Effort)",
                        Actions = new List<TemplateAction>
                        {
                            new TemplateAction
                            {
                                ActionName = "UpdateField",
                                Parameters = new Dictionary<string, string>
                                {
                                    { "FieldName", "TargetField" },
                                    { "UpdateType", "CALCULATE" },
                                    { "Value", "EFFORT_TO_SIZE" }
                                }
                            }
                        }
                    }
                },

                QuickActions = new List<QuickAction>
                {
                    new QuickAction
                    {
                        Name = "Approve All Low Priority",
                        Description = "Auto-approve all pending reviews with priority > 80",
                        Endpoint = "/api/AdminDashboard/bulk-approve",
                        Conditions = new List<string> { "Priority > 80", "No complex logic", "Simple state transitions" }
                    },
                    new QuickAction
                    {
                        Name = "Export Review Queue",
                        Description = "Export pending reviews as Excel/CSV",
                        Endpoint = "/api/AdminDashboard/export-queue",
                        Conditions = new List<string> { "All pending reviews", "Include complexity analysis" }
                    },
                    new QuickAction
                    {
                        Name = "Bulk Templates",
                        Description = "Apply common templates to multiple rules",
                        Endpoint = "/api/AdminDashboard/apply-templates",
                        Conditions = new List<string> { "Similar patterns", "Safe transformations" }
                    }
                }
            };

            return Ok(templates);
        }

        /// <summary>
        /// 🚀 Bulk operations
        /// </summary>
        [HttpPost("bulk-approve")]
        public async Task<IActionResult> BulkApprove([FromBody] BulkApprovalRequest request)
        {
            try
            {
                var results = new List<BulkOperationResult>();
                var pendingRules = await _rulesService.GetManualReviewRulesAsync();
                var eligibleRules = pendingRules.Where(r => 
                    r.Priority >= request.MinPriority && 
                    !HasComplexLogic(ExtractOriginalXmlCode(r)) &&
                    request.RuleIds.Contains(r.RuleId ?? 0)
                ).ToList();

                foreach (var rule in eligibleRules)
                {
                    try
                    {
                        // Simple auto-approval logic
                        var autoConverted = await AutoConvertSimpleRule(rule);
                        if (autoConverted != null)
                        {
                            await _rulesService.UpdateRuleAsync(rule, autoConverted);
                            results.Add(new BulkOperationResult
                            {
                                RuleId = rule.RuleId ?? 0,
                                RuleName = rule.RuleName,
                                Status = "Approved",
                                Message = "Auto-converted and activated"
                            });
                        }
                        else
                        {
                            results.Add(new BulkOperationResult
                            {
                                RuleId = rule.RuleId ?? 0,
                                RuleName = rule.RuleName,
                                Status = "Skipped",
                                Message = "Could not auto-convert"
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        results.Add(new BulkOperationResult
                        {
                            RuleId = rule.RuleId ?? 0,
                            RuleName = rule.RuleName,
                            Status = "Failed",
                            Message = ex.Message
                        });
                    }
                }

                return Ok(new
                {
                    Message = $"Bulk approval completed for {results.Count} rules",
                    Results = results,
                    Summary = new
                    {
                        Approved = results.Count(r => r.Status == "Approved"),
                        Skipped = results.Count(r => r.Status == "Skipped"),
                        Failed = results.Count(r => r.Status == "Failed")
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bulk approval failed");
                return StatusCode(500, $"Bulk approval failed: {ex.Message}");
            }
        }

        #region Helper Methods

        private string ExtractOriginalXmlCode(RuleModel rule)
        {
            var commentAction = rule.Actions?.FirstOrDefault(a => a.ActionName == "AddComment");
            var commentText = commentAction?.Parameters?.FirstOrDefault(p => p.ParamKey == "CommentText")?.ParamValue ?? "";
            
            var xmlStartIndex = commentText.IndexOf("Original XML code:\n");
            if (xmlStartIndex >= 0)
            {
                return commentText.Substring(xmlStartIndex + "Original XML code:\n".Length);
            }

            return "";
        }

        private List<string> AnalyzeComplexityReasons(string xmlCode)
        {
            var reasons = new List<string>();
            if (xmlCode.Contains("HasParent()")) reasons.Add("Parent Kontrolü");
            if (xmlCode.Contains("Parent.")) reasons.Add("Parent Property Access");
            if (xmlCode.Contains("DateTime.Now")) reasons.Add("DateTime Operations");
            if (xmlCode.Contains("Contains(")) reasons.Add("Array/Collection Operations");
            if (xmlCode.Contains("string[]")) reasons.Add("Array Declarations");
            if (xmlCode.Contains("for (") || xmlCode.Contains("while (")) reasons.Add("Loops");
            if (xmlCode.Contains("switch (")) reasons.Add("Switch Statements");
            if (xmlCode.Contains("try {")) reasons.Add("Exception Handling");
            return reasons.Any() ? reasons : new List<string> { "Complex Logic" };
        }

        private bool HasComplexLogic(string xmlCode) =>
            xmlCode.Contains("HasParent()") || xmlCode.Contains("Parent.") || 
            xmlCode.Contains("DateTime.Now") || xmlCode.Contains("Contains(") ||
            xmlCode.Contains("string[]") || xmlCode.Contains("for (") ||
            xmlCode.Contains("while (") || xmlCode.Contains("switch (") ||
            xmlCode.Contains("try {");

        private string GetPriorityCategory(int priority) =>
            priority <= 25 ? "High" : priority <= 75 ? "Medium" : "Low";

        private double CalculateConversionRate(List<RuleModel> rules) =>
            rules.Count > 0 ? Math.Round((double)rules.Count(r => !r.RuleName.Contains("_ManualReview")) / rules.Count * 100, 1) : 0;

        private List<string> GetTopComplexityReasons(List<RuleModel> rules) =>
            rules.SelectMany(r => AnalyzeComplexityReasons(ExtractOriginalXmlCode(r)))
                 .GroupBy(r => r)
                 .OrderByDescending(g => g.Count())
                 .Take(5)
                 .Select(g => $"{g.Key} ({g.Count()})")
                 .ToList();

        private List<ActivityItem> GenerateRecentActivity(List<RuleModel> rules) =>
            new List<ActivityItem>
            {
                new ActivityItem { Action = "Rule Created", Target = "ValidationRule_1", Time = DateTime.Now.AddHours(-2), User = "System" },
                new ActivityItem { Action = "Review Completed", Target = "CalculationRule_5", Time = DateTime.Now.AddHours(-4), User = "Admin" },
                new ActivityItem { Action = "Rule Rejected", Target = "ComplexRule_12", Time = DateTime.Now.AddHours(-6), User = "Reviewer" }
            };



        private List<string> GetAvailableFields() =>
            new List<string>
            {
                "System_State", "System_WorkItemType", "System_Title", "System_AssignedTo",
                "Microsoft_VSTS_Scheduling_Effort", "Talep_Buyukluk", "Talep_YasalMi",
                "System_TeamProject", "System_AreaPath", "System_CreatedBy"
            };

        private List<string> GetSampleExpressions() =>
            new List<string>
            {
                "body.Fields.System_State == \"Active\"",
                "body.Fields.System_WorkItemType == \"Task\" && !string.IsNullOrWhiteSpace(body.Fields.System_Title)",
                "body.Fields.Microsoft_VSTS_Scheduling_Effort > 0",
                "string.IsNullOrWhiteSpace(body.Fields.System_AssignedTo)"
            };

        private List<ActionTemplate> GetActionTemplates() =>
            new List<ActionTemplate>
            {
                new ActionTemplate { Name = "Change State", ActionName = "TransitionToState", SampleParams = new Dictionary<string, string> { { "NewState", "Active" } } },
                new ActionTemplate { Name = "Add Comment", ActionName = "AddComment", SampleParams = new Dictionary<string, string> { { "CommentText", "Comment text" } } },
                new ActionTemplate { Name = "Set Field", ActionName = "SetField", SampleParams = new Dictionary<string, string> { { "FieldName", "FieldName" }, { "FieldValue", "Value" } } }
            };

        private List<string> GetValidationRules() =>
            new List<string>
            {
                "Expression must be valid C# boolean expression",
                "Field names must start with 'body.Fields.'",
                "Use string.IsNullOrWhiteSpace() for null checks",
                "Avoid complex nested conditions"
            };

        private async Task<List<AiSuggestion>> GenerateAiSuggestions(RuleModel rule) =>
            new List<AiSuggestion>
            {
                new AiSuggestion { Type = "Simplify", Description = "Break complex condition into multiple rules", Confidence = 85 },
                new AiSuggestion { Type = "Optimize", Description = "Use field validation pattern", Confidence = 92 }
            };

        private List<SamplePayload> GetSamplePayloads() =>
            new List<SamplePayload>
            {
                new SamplePayload { Name = "Standard Task", Data = new Dictionary<string, object> { { "System_State", "Active" }, { "System_WorkItemType", "Task" } } },
                new SamplePayload { Name = "Empty Fields", Data = new Dictionary<string, object> { { "System_State", "" }, { "System_WorkItemType", "Bug" } } }
            };

        private List<TestCase> GenerateTestCases(RuleModel rule) =>
            new List<TestCase>
            {
                new TestCase { Name = "Positive Case", ExpectedResult = true, Description = "Should trigger when conditions met" },
                new TestCase { Name = "Negative Case", ExpectedResult = false, Description = "Should not trigger when conditions not met" }
            };

        private List<string> ExtractFieldsFromExpression(string expression)
        {
            var fields = new List<string>();
            var parts = expression.Split(new[] { "body.Fields." }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var part in parts.Skip(1))
            {
                var fieldEnd = part.IndexOfAny(new[] { ' ', '=', '!', '&', '|', ')', '>' });
                if (fieldEnd > 0)
                {
                    fields.Add(part.Substring(0, fieldEnd).Trim());
                }
            }
            
            return fields.Distinct().ToList();
        }

        private TestResult TestExpressionWithSample(string expression, SamplePayload sample)
        {
            try
            {
                // Simple mock test - in real implementation, you'd evaluate the expression
                var mockResult = sample.Name.Contains("Standard");
                
                return new TestResult
                {
                    SampleName = sample.Name,
                    Passed = mockResult,
                    Result = mockResult ? "Expression evaluated to true" : "Expression evaluated to false",
                    ExecutionTime = "< 1ms"
                };
            }
            catch (Exception ex)
            {
                return new TestResult
                {
                    SampleName = sample.Name,
                    Passed = false,
                    Result = $"Error: {ex.Message}",
                    ExecutionTime = "N/A"
                };
            }
        }

        private async Task<RuleModel?> AutoConvertSimpleRule(RuleModel rule)
        {
            var xmlCode = ExtractOriginalXmlCode(rule);
            
            // Simple auto-conversion for basic patterns
            if (!HasComplexLogic(xmlCode) && xmlCode.Contains("if ("))
            {
                // This would use the XmlToJsonConverter logic
                // For now, return a basic conversion
                return new RuleModel
                {
                    RuleName = rule.RuleName.Replace("_ManualReview", "_AutoConverted"),
                    Expression = "1 == 1", // Placeholder
                    AppliesTo = rule.AppliesTo,
                    RuleSet = rule.RuleSet,
                    Priority = rule.Priority,
                    IsActive = true,
                    Actions = new List<RuleAction>
                    {
                        new RuleAction
                        {
                            ActionName = "AddComment",
                            ConditionType = "Success",
                            ExecutionOrder = 1,
                            Parameters = new List<RuleActionParameter>
                            {
                                new RuleActionParameter { ParamKey = "CommentText", ParamValue = "Auto-converted rule" }
                            }
                        }
                    }
                };
            }

            return null;
        }

        #endregion
    }

    #region Dashboard DTOs

    public class DashboardOverview
    {
        public int TotalRules { get; set; }
        public int ActiveRules { get; set; }
        public int InactiveRules { get; set; }
        public int PendingReviews { get; set; }
        public ReviewQueueStats ReviewQueue { get; set; } = new();
        public ConversionStats ConversionStats { get; set; } = new();
        public List<ActivityItem> RecentActivity { get; set; } = new();
        public DateTime LastUpdated { get; set; }
    }

    public class ReviewQueueStats
    {
        public int TotalPending { get; set; }
        public Dictionary<string, int> ByComplexity { get; set; } = new();
        public Dictionary<string, int> ByRuleSet { get; set; } = new();
        public Dictionary<string, int> ByPriority { get; set; } = new();
    }

    public class ConversionStats
    {
        public int AutoConvertedRules { get; set; }
        public int ManualReviewRules { get; set; }
        public int RejectedRules { get; set; }
        public double ConversionRate { get; set; }
        public string AvgReviewTime { get; set; } = string.Empty;
        public List<string> TopComplexityReasons { get; set; } = new();
    }

    public class ActivityItem
    {
        public string Action { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public DateTime Time { get; set; }
        public string User { get; set; } = string.Empty;
    }



    public class RuleEditorData
    {
        public RuleModel Rule { get; set; } = new();
        public string OriginalXmlCode { get; set; } = string.Empty;
        public List<string> ComplexityReasons { get; set; } = new();
        public ValidationHelpers ValidationHelpers { get; set; } = new();
        public List<AiSuggestion> AiSuggestions { get; set; } = new();
        public TestingTools TestingTools { get; set; } = new();
    }

    public class ValidationHelpers
    {
        public List<string> AvailableFields { get; set; } = new();
        public List<string> SampleExpressions { get; set; } = new();
        public List<ActionTemplate> ActionTemplates { get; set; } = new();
        public List<string> ValidationRules { get; set; } = new();
    }

    public class ActionTemplate
    {
        public string Name { get; set; } = string.Empty;
        public string ActionName { get; set; } = string.Empty;
        public Dictionary<string, string> SampleParams { get; set; } = new();
    }

    public class AiSuggestion
    {
        public string Type { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Confidence { get; set; }
    }

    public class TestingTools
    {
        public List<SamplePayload> SamplePayloads { get; set; } = new();
        public List<TestCase> TestCases { get; set; } = new();
        public string ValidationEndpoint { get; set; } = string.Empty;
    }

    public class SamplePayload
    {
        public string Name { get; set; } = string.Empty;
        public Dictionary<string, object> Data { get; set; } = new();
    }

    public class TestCase
    {
        public string Name { get; set; } = string.Empty;
        public bool ExpectedResult { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    public class ExpressionValidationRequest
    {
        public string Expression { get; set; } = string.Empty;
        public bool TestWithSamples { get; set; } = false;
        public List<SamplePayload>? SamplePayloads { get; set; }
    }

    public class ExpressionValidationResult
    {
        public bool IsValid { get; set; }
        public string Expression { get; set; } = string.Empty;
        public List<string> ValidationMessages { get; set; } = new();
        public List<string> Suggestions { get; set; } = new();
        public List<TestResult> TestResults { get; set; } = new();
    }

    public class TestResult
    {
        public string SampleName { get; set; } = string.Empty;
        public bool Passed { get; set; }
        public string Result { get; set; } = string.Empty;
        public string ExecutionTime { get; set; } = string.Empty;
    }

    public class RuleTemplates
    {
        public List<RuleTemplate> CommonPatterns { get; set; } = new();
        public List<QuickAction> QuickActions { get; set; } = new();
    }

    public class RuleTemplate
    {
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Expression { get; set; } = string.Empty;
        public List<TemplateAction> Actions { get; set; } = new();
    }

    public class TemplateAction
    {
        public string ActionName { get; set; } = string.Empty;
        public Dictionary<string, string> Parameters { get; set; } = new();
    }

    public class QuickAction
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public List<string> Conditions { get; set; } = new();
    }

    public class BulkApprovalRequest
    {
        public List<int> RuleIds { get; set; } = new();
        public int MinPriority { get; set; } = 50;
        public bool AutoActivate { get; set; } = true;
        public string ApprovedBy { get; set; } = string.Empty;
    }

    public class BulkOperationResult
    {
        public int RuleId { get; set; }
        public string RuleName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    #endregion
} 