using BurganAzureDevopsAggregator.Business;
using BurganAzureDevopsAggregator.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BurganAzureDevopsAggregator.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ManualReviewController : ControllerBase
    {
        private readonly RulesService _rulesService;
        private readonly ILogger<ManualReviewController> _logger;

        public ManualReviewController(RulesService rulesService, ILogger<ManualReviewController> logger)
        {
            _rulesService = rulesService;
            _logger = logger;
        }

        /// <summary>
        /// Manuel review bekleyen rule'ları listeler
        /// </summary>
        [HttpGet("pending")]
        public async Task<IActionResult> GetPendingReviews()
        {
            try
            {
                var pendingRules = await _rulesService.GetManualReviewRulesAsync();
                
                var reviewItems = pendingRules.Select(rule => new ManualReviewItem
                {
                    RuleId = rule.RuleId,
                    RuleName = rule.RuleName,
                    OriginalXmlCode = ExtractOriginalXmlCode(rule),
                    AppliesTo = rule.AppliesTo,
                    RuleSet = rule.RuleSet,
                    Priority = rule.Priority,
                    CreatedDate = DateTime.Now, // This should come from database
                    ReviewStatus = "Pending",
                    ComplexityReasons = AnalyzeComplexityReasons(ExtractOriginalXmlCode(rule))
                }).ToList();

                return Ok(new
                {
                    Message = $"Found {reviewItems.Count} rules pending manual review",
                    PendingReviews = reviewItems,
                    Summary = new
                    {
                        TotalPending = reviewItems.Count,
                        ByComplexity = reviewItems.GroupBy(r => r.ComplexityReasons.FirstOrDefault() ?? "Unknown")
                                                 .ToDictionary(g => g.Key, g => g.Count())
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get pending reviews");
                return StatusCode(500, $"Error retrieving pending reviews: {ex.Message}");
            }
        }

        /// <summary>
        /// Belirli bir rule için template önerileri getirir
        /// </summary>
        [HttpGet("{ruleId}/suggestions")]
        public async Task<IActionResult> GetRuleSuggestions(int ruleId)
        {
            try
            {
                var rule = await _rulesService.GetRuleByIdAsync(ruleId);
                if (rule == null)
                {
                    return NotFound($"Rule with ID {ruleId} not found");
                }

                var originalXml = ExtractOriginalXmlCode(rule);
                var suggestions = GenerateRuleSuggestions(originalXml, rule);

                return Ok(new
                {
                    RuleId = ruleId,
                    RuleName = rule.RuleName,
                    OriginalXmlCode = originalXml,
                    Suggestions = suggestions
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to generate suggestions for rule {ruleId}");
                return StatusCode(500, $"Error generating suggestions: {ex.Message}");
            }
        }

        /// <summary>
        /// Manuel review sonucunu işler ve rule'ı günceller
        /// </summary>
        [HttpPost("{ruleId}/complete")]
        public async Task<IActionResult> CompleteReview(int ruleId, [FromBody] ManualReviewCompletion completion)
        {
            try
            {
                var existingRule = await _rulesService.GetRuleByIdAsync(ruleId);
                if (existingRule == null)
                {
                    return NotFound($"Rule with ID {ruleId} not found");
                }

                // Validation
                if (string.IsNullOrWhiteSpace(completion.ReviewedExpression))
                {
                    return BadRequest("Reviewed expression is required");
                }

                if (completion.ReviewedActions == null || !completion.ReviewedActions.Any())
                {
                    return BadRequest("At least one action is required");
                }

                // Update rule with reviewed content
                var updatedRule = new RuleModel
                {
                    RuleId = ruleId,
                    RuleName = completion.NewRuleName ?? existingRule.RuleName.Replace("_ManualReview", ""),
                    Expression = completion.ReviewedExpression,
                    AppliesTo = completion.AppliesTo ?? existingRule.AppliesTo,
                    RuleSet = completion.RuleSet ?? existingRule.RuleSet,
                    Priority = completion.Priority ?? existingRule.Priority,
                    IsActive = completion.ActivateImmediately,
                    Actions = completion.ReviewedActions.Select((action, index) => new RuleAction
                    {
                        ActionName = action.ActionName,
                        ConditionType = action.ConditionType ?? "Success",
                        ExecutionOrder = action.ExecutionOrder ?? (index + 1),
                        Parameters = action.Parameters?.Select(p => new RuleActionParameter
                        {
                            ParamKey = p.ParamKey,
                            ParamValue = p.ParamValue
                        }).ToList() ?? new List<RuleActionParameter>()
                    }).ToList()
                };

                // Save updated rule
                await _rulesService.UpdateRuleAsync(existingRule, updatedRule);

                // Log review completion
                _logger.LogInformation($"✅ Manual review completed for rule {ruleId} by reviewer: {completion.ReviewerName}");

                // Add review comment
                if (!string.IsNullOrEmpty(completion.ReviewNotes))
                {
                    var reviewComment = new RuleAction
                    {
                        ActionName = "AddComment",
                        ConditionType = "Success",
                        ExecutionOrder = 999,
                        Parameters = new List<RuleActionParameter>
                        {
                            new RuleActionParameter 
                            { 
                                ParamKey = "CommentText", 
                                ParamValue = $"MANUAL REVIEW COMPLETED by {completion.ReviewerName}: {completion.ReviewNotes}" 
                            }
                        }
                    };
                    updatedRule.Actions.Add(reviewComment);
                }

                return Ok(new
                {
                    Message = "Manual review completed successfully",
                    RuleId = ruleId,
                    RuleName = updatedRule.RuleName,
                    IsActive = updatedRule.IsActive,
                    ReviewedBy = completion.ReviewerName,
                    ReviewDate = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to complete review for rule {ruleId}");
                return StatusCode(500, $"Error completing review: {ex.Message}");
            }
        }

        /// <summary>
        /// Rule'ı reject eder (silme veya devre dışı bırakma)
        /// </summary>
        [HttpPost("{ruleId}/reject")]
        public async Task<IActionResult> RejectRule(int ruleId, [FromBody] RejectRuleRequest request)
        {
            try
            {
                var rule = await _rulesService.GetRuleByIdAsync(ruleId);
                if (rule == null)
                {
                    return NotFound($"Rule with ID {ruleId} not found");
                }

                if (request.DeleteRule)
                {
                    await _rulesService.DeleteRuleAsync(ruleId);
                    _logger.LogInformation($"🗑️ Rule {ruleId} deleted after manual review rejection");
                }
                else
                {
                    rule.IsActive = false;
                    rule.RuleName += "_Rejected";
                    await _rulesService.UpdateRuleAsync(rule, rule);
                    _logger.LogInformation($"❌ Rule {ruleId} rejected and deactivated");
                }

                return Ok(new
                {
                    Message = request.DeleteRule ? "Rule deleted successfully" : "Rule rejected and deactivated",
                    RuleId = ruleId,
                    Action = request.DeleteRule ? "Deleted" : "Rejected",
                    ReviewedBy = request.ReviewerName,
                    Reason = request.RejectionReason
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to reject rule {ruleId}");
                return StatusCode(500, $"Error rejecting rule: {ex.Message}");
            }
        }

        #region Helper Methods

        private string ExtractOriginalXmlCode(RuleModel rule)
        {
            var commentAction = rule.Actions?.FirstOrDefault(a => a.ActionName == "AddComment");
            var commentText = commentAction?.Parameters?.FirstOrDefault(p => p.ParamKey == "CommentText")?.ParamValue ?? "";
            
            // Extract XML from comment text
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

        private List<RuleSuggestion> GenerateRuleSuggestions(string xmlCode, RuleModel rule)
        {
            var suggestions = new List<RuleSuggestion>();

            // Suggestion 1: Multiple Simple Rules
            suggestions.Add(new RuleSuggestion
            {
                Type = "Split into Multiple Rules",
                Description = "Break down complex logic into multiple simple rules",
                SuggestedRules = GenerateSplitRuleSuggestions(xmlCode, rule)
            });

            // Suggestion 2: Conditional Expression
            if (xmlCode.Contains("if (") && !xmlCode.Contains("HasParent()"))
            {
                suggestions.Add(new RuleSuggestion
                {
                    Type = "Simplified Boolean Expression",
                    Description = "Convert IF condition to boolean expression",
                    SuggestedRules = new List<SuggestedRule>
                    {
                        new SuggestedRule
                        {
                            RuleName = rule.RuleName.Replace("_ManualReview", "_Simplified"),
                            Expression = "/* TODO: Extract and simplify IF condition */",
                            Actions = new List<SuggestedAction>
                            {
                                new SuggestedAction { ActionName = "AddComment", ParamKey = "CommentText", ParamValue = "Simplified version - review needed" }
                            }
                        }
                    }
                });
            }

            // Suggestion 3: Custom Action
            suggestions.Add(new RuleSuggestion
            {
                Type = "Custom Action Handler",
                Description = "Implement custom action handler for complex business logic",
                SuggestedRules = new List<SuggestedRule>
                {
                    new SuggestedRule
                    {
                        RuleName = rule.RuleName.Replace("_ManualReview", "_CustomAction"),
                        Expression = "1 == 1", // Always execute
                        Actions = new List<SuggestedAction>
                        {
                            new SuggestedAction 
                            { 
                                ActionName = "ExecuteCustomLogic", 
                                ParamKey = "LogicType", 
                                ParamValue = rule.RuleName.Replace("_ManualReview", "") 
                            }
                        }
                    }
                }
            });

            return suggestions;
        }

        private List<SuggestedRule> GenerateSplitRuleSuggestions(string xmlCode, RuleModel rule)
        {
            var splitRules = new List<SuggestedRule>();

            // Analyze and suggest splits based on logical operators
            if (xmlCode.Contains("&&") || xmlCode.Contains("||"))
            {
                splitRules.Add(new SuggestedRule
                {
                    RuleName = $"{rule.RuleName.Replace("_ManualReview", "")}_Step1",
                    Expression = "/* TODO: First condition */",
                    Actions = new List<SuggestedAction>
                    {
                        new SuggestedAction { ActionName = "SetField", ParamKey = "FieldName", ParamValue = "Custom.Step1Completed" }
                    }
                });

                splitRules.Add(new SuggestedRule
                {
                    RuleName = $"{rule.RuleName.Replace("_ManualReview", "")}_Step2",
                    Expression = "body.Fields.Custom_Step1Completed == \"true\" && /* TODO: Second condition */",
                    Actions = new List<SuggestedAction>
                    {
                        new SuggestedAction { ActionName = "TransitionToState", ParamKey = "NewState", ParamValue = "/* TODO: Target State */" }
                    }
                });
            }

            return splitRules;
        }

        #endregion
    }

    #region DTOs

    public class ManualReviewItem
    {
        public int? RuleId { get; set; }
        public string RuleName { get; set; } = string.Empty;
        public string OriginalXmlCode { get; set; } = string.Empty;
        public string AppliesTo { get; set; } = string.Empty;
        public string RuleSet { get; set; } = string.Empty;
        public int Priority { get; set; }
        public DateTime CreatedDate { get; set; }
        public string ReviewStatus { get; set; } = "Pending";
        public List<string> ComplexityReasons { get; set; } = new List<string>();
    }

    public class ManualReviewCompletion
    {
        public string? NewRuleName { get; set; }
        public string ReviewedExpression { get; set; } = string.Empty;
        public string? AppliesTo { get; set; }
        public string? RuleSet { get; set; }
        public int? Priority { get; set; }
        public bool ActivateImmediately { get; set; } = false;
        public List<ReviewedAction> ReviewedActions { get; set; } = new List<ReviewedAction>();
        public string ReviewerName { get; set; } = string.Empty;
        public string? ReviewNotes { get; set; }
    }

    public class ReviewedAction
    {
        public string ActionName { get; set; } = string.Empty;
        public string? ConditionType { get; set; }
        public int? ExecutionOrder { get; set; }
        public List<ReviewedParameter>? Parameters { get; set; }
    }

    public class ReviewedParameter
    {
        public string ParamKey { get; set; } = string.Empty;
        public string ParamValue { get; set; } = string.Empty;
    }

    public class RejectRuleRequest
    {
        public bool DeleteRule { get; set; } = false;
        public string RejectionReason { get; set; } = string.Empty;
        public string ReviewerName { get; set; } = string.Empty;
    }

    public class RuleSuggestion
    {
        public string Type { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<SuggestedRule> SuggestedRules { get; set; } = new List<SuggestedRule>();
    }

    public class SuggestedRule
    {
        public string RuleName { get; set; } = string.Empty;
        public string Expression { get; set; } = string.Empty;
        public List<SuggestedAction> Actions { get; set; } = new List<SuggestedAction>();
    }

    public class SuggestedAction
    {
        public string ActionName { get; set; } = string.Empty;
        public string ParamKey { get; set; } = string.Empty;
        public string ParamValue { get; set; } = string.Empty;
    }

    #endregion
} 