using BurganAzureDevopsAggregator.Models;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Linq;

namespace BurganAzureDevopsAggregator.Business
{
    /// <summary>
    /// XML rule'ları boolean expression + actions formatına dönüştürür
    /// </summary>
    public class XmlToJsonConverter
    {
        private readonly ILogger<XmlToJsonConverter> _logger;

        public XmlToJsonConverter(ILogger<XmlToJsonConverter> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// XML content'i parse ederek RuleModel listesi döner
        /// </summary>
        public ConversionResult ConvertXmlToRules(string xmlContent, string ruleSet = "ConvertedRules", int priority = 100)
        {
            var result = new ConversionResult();

            try
            {
                var xmlDoc = XDocument.Parse(xmlContent);
                var ruleElements = xmlDoc.Descendants("rule");

                foreach (var ruleElement in ruleElements)
                {
                    var ruleName = ruleElement.Attribute("name")?.Value ?? "UnnamedRule";
                    var appliesTo = ruleElement.Attribute("appliesTo")?.Value ?? "All";
                    var csharpCode = ruleElement.Value.Trim();

                    _logger.LogInformation($"📄 Converting rule: {ruleName}");

                    var conversionAttempt = ConvertSingleRule(ruleName, appliesTo, csharpCode, ruleSet, priority);
                    result.ConvertedRules.AddRange(conversionAttempt.Rules);
                    result.ConversionNotes.AddRange(conversionAttempt.Notes);
                }

                result.IsSuccess = true;
                _logger.LogInformation($"✅ Conversion completed: {result.ConvertedRules.Count} rules converted");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ XML conversion failed");
                result.IsSuccess = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Tek bir XML rule'ı convert eder
        /// </summary>
        private SingleRuleConversionResult ConvertSingleRule(string ruleName, string appliesTo, string csharpCode, string ruleSet, int priority)
        {
            var result = new SingleRuleConversionResult();

            try
            {
                // C# kodunu analiz et
                var analysis = AnalyzeCSharpCode(csharpCode);

                if (analysis.HasSimpleCondition)
                {
                    // Basit condition → Boolean expression'a çevir
                    var rule = CreateBooleanExpressionRule(ruleName, appliesTo, ruleSet, priority, analysis);
                    result.Rules.Add(rule);
                    result.Notes.Add($"✅ {ruleName}: Successfully converted to boolean expression");
                }
                else if (analysis.HasCalculation)
                {
                    // Calculation logic → UpdateField action'a çevir
                    var rule = CreateCalculationRule(ruleName, appliesTo, ruleSet, priority, analysis);
                    result.Rules.Add(rule);
                    result.Notes.Add($"✅ {ruleName}: Converted to calculation rule");
                }
                else
                {
                    // Karmaşık logic → Manuel review gerekiyor
                    var rule = CreateManualReviewRule(ruleName, appliesTo, ruleSet, priority, csharpCode);
                    result.Rules.Add(rule);
                    result.Notes.Add($"⚠️ {ruleName}: Complex logic - manual review required");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"⚠️ Failed to convert rule {ruleName}: {ex.Message}");
                result.Notes.Add($"❌ {ruleName}: Conversion failed - {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// C# kodunu analiz eder
        /// </summary>
        private CodeAnalysis AnalyzeCSharpCode(string code)
        {
            var analysis = new CodeAnalysis();

            // Remove comments and normalize
            var cleanCode = Regex.Replace(code, @"//.*?$", "", RegexOptions.Multiline);
            cleanCode = Regex.Replace(cleanCode, @"/\*.*?\*/", "", RegexOptions.Singleline);
            cleanCode = cleanCode.Replace("\r\n", "\n").Replace("\r", "\n");

            // Condition patterns
            analysis.IfConditions = ExtractIfConditions(cleanCode);
            analysis.HasSimpleCondition = analysis.IfConditions.Any() && !HasComplexLogic(cleanCode);

            // Assignment patterns
            analysis.FieldAssignments = ExtractFieldAssignments(cleanCode);
            analysis.StateTransitions = ExtractStateTransitions(cleanCode);

            // Calculation patterns
            analysis.HasCalculation = HasCalculationLogic(cleanCode);
            analysis.CalculationFields = ExtractCalculationFields(cleanCode);

            return analysis;
        }

        /// <summary>
        /// If condition'larını extract eder (parantez tarama yaklaşımı - tüm kod üzerinde)
        /// </summary>
        private List<string> ExtractIfConditions(string code)
        {
            var conditions = new List<string>();
            
            // Tüm code'u tek satır gibi normalize et
            code = code.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
            
            int index = 0;
            while (index < code.Length)
            {
                int ifIndex = code.IndexOf("if", index, StringComparison.OrdinalIgnoreCase);
                if (ifIndex == -1) break;

                // "if" ten sonra hemen '(' aranır
                int parenIndex = code.IndexOf('(', ifIndex);
                if (parenIndex == -1) break;

                // Parantez içini çıkar
                string condition = ExtractConditionFromParentheses(code, parenIndex);
                if (!string.IsNullOrWhiteSpace(condition))
                {
                    conditions.Add(condition.Trim());
                    _logger.LogDebug($"🔍 Found if condition: {condition}");
                }

                // Sonraki arama için index'i parantez kapanışının ötesine al
                int closeIndex = FindClosingParenIndex(code, parenIndex);
                index = closeIndex > parenIndex ? closeIndex + 1 : parenIndex + 1;
            }

            _logger.LogInformation($"📊 Total if conditions found: {conditions.Count}");
            return conditions;
        }

        private int FindClosingParenIndex(string text, int openIndex)
        {
            int depth = 0;
            for (int i = openIndex; i < text.Length; i++)
            {
                if (text[i] == '(') depth++;
                else if (text[i] == ')')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }
            return openIndex;
        }

        /// <summary>
        /// Parantezler arası condition'ı extract eder (nested parantezleri handle eder)
        /// </summary>
        private string ExtractConditionFromParentheses(string text, int startIndex)
        {
            int openCount = 0;
            int start = startIndex + 1; // '(' sonrası
            int end = start;
            
            for (int i = startIndex; i < text.Length; i++)
            {
                if (text[i] == '(')
                    openCount++;
                else if (text[i] == ')')
                {
                    openCount--;
                    if (openCount == 0)
                    {
                        end = i;
                        break;
                    }
                }
            }
            
            if (end > start)
            {
                return text.Substring(start, end - start).Trim();
            }
            
            return string.Empty;
        }

        /// <summary>
        /// Field assignment'ları extract eder (sadece gerçek assignment'lar)
        /// </summary>
        private List<FieldAssignment> ExtractFieldAssignments(string code)
        {
            var assignments = new List<FieldAssignment>();

            // Sadece actual assignment satırlarını bul
            var lines = code.Split('\n').Select(l => l.Trim()).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            
            foreach (var line in lines)
            {
                // self["FieldName"] = value; pattern'i (strict)
                if (Regex.IsMatch(line, @"^\s*self\[""[^""]+\""\]\s*=\s*[^;]+;\s*$"))
                {
                    var match = Regex.Match(line, @"^\s*self\[""([^""]+)\""\]\s*=\s*(.+?);\s*$");
                    if (match.Success)
                    {
                        assignments.Add(new FieldAssignment
                        {
                            FieldName = match.Groups[1].Value,
                            Value = match.Groups[2].Value.Trim()
                        });
                    }
                }
                // self.Fields["FieldName"].Value = value; pattern'i (strict)
                else if (Regex.IsMatch(line, @"^\s*self\.Fields\[""[^""]+\""\]\.Value\s*=\s*[^;]+;\s*$"))
                {
                    var match = Regex.Match(line, @"^\s*self\.Fields\[""([^""]+)\""\]\.Value\s*=\s*(.+?);\s*$");
                    if (match.Success)
                    {
                        assignments.Add(new FieldAssignment
                        {
                            FieldName = match.Groups[1].Value,
                            Value = match.Groups[2].Value.Trim()
                        });
                    }
                }
            }

            return assignments;
        }

        /// <summary>
        /// State transition'ları extract eder
        /// </summary>
        private List<StateTransition> ExtractStateTransitions(string code)
        {
            var transitions = new List<StateTransition>();

            // Pattern: self.TransitionToState("NewState", "Comment")
            var matches = Regex.Matches(code, @"self\.TransitionToState\(\s*""([^""]+)""\s*,\s*""([^""]*)""\s*\)");
            
            foreach (Match match in matches)
            {
                transitions.Add(new StateTransition
                {
                    NewState = match.Groups[1].Value,
                    Comment = match.Groups[2].Value
                });
            }

            return transitions;
        }

        /// <summary>
        /// Boolean expression rule oluşturur
        /// </summary>
        private RuleModel CreateBooleanExpressionRule(string ruleName, string appliesTo, string ruleSet, int priority, CodeAnalysis analysis)
        {
            var expression = "1 == 1"; // Default: always execute

            if (analysis.IfConditions.Any())
            {
                // İlk condition'ı boolean expression'a çevir
                expression = ConvertConditionToExpression(analysis.IfConditions.First());
            }

            var actions = new List<RuleAction>();

            var executionOrder = 1;

            // State transitions → TransitionToState action (mevcut yapınızla uyumlu)
            foreach (var transition in analysis.StateTransitions)
            {
                var parameters = new List<RuleActionParameter>
                {
                    new RuleActionParameter { ParamKey = "NewState", ParamValue = transition.NewState }
                };

                // Comment parametresi (optional)
                if (!string.IsNullOrWhiteSpace(transition.Comment))
                {
                    parameters.Add(new RuleActionParameter { ParamKey = "Comment", ParamValue = transition.Comment });
                }

                // Reason parametresi de eklenebilir (optional, TransitionToStateActionHandler destekliyor)
                // Şimdilik Comment kullanıyoruz ama isterseniz Reason da ayrı olabilir
                
                actions.Add(new RuleAction
                {
                    ActionName = "TransitionToState",
                    ConditionType = "Success",
                    ExecutionOrder = executionOrder++,
                    Parameters = parameters
                });
            }

            // Field assignments → SetField action
            foreach (var assignment in analysis.FieldAssignments)
            {
                actions.Add(new RuleAction
                {
                    ActionName = "SetField",
                    ConditionType = "Success",
                    ExecutionOrder = executionOrder++,
                    Parameters = new List<RuleActionParameter>
                    {
                        new RuleActionParameter { ParamKey = "FieldName", ParamValue = assignment.FieldName },
                        new RuleActionParameter { ParamKey = "FieldValue", ParamValue = ConvertValueToLiteral(assignment.Value) }
                    }
                });
            }

            return new RuleModel
            {
                RuleName = ruleName,
                Expression = expression,
                AppliesTo = appliesTo,
                RuleSet = ruleSet,
                Priority = priority,
                IsActive = true,
                Actions = actions
            };
        }

        /// <summary>
        /// Calculation rule oluşturur (TalepBuyukluguUpdate örneği için)
        /// </summary>
        private RuleModel CreateCalculationRule(string ruleName, string appliesTo, string ruleSet, int priority, CodeAnalysis analysis)
        {
            var expression = "body.Fields.Microsoft_VSTS_Scheduling_Effort != null && body.Fields.Microsoft_VSTS_Scheduling_Effort != 0";

            var actions = new List<RuleAction>
            {
                new RuleAction
                {
                    ActionName = "UpdateField",
                    ConditionType = "Success",
                    ExecutionOrder = 1,
                    Parameters = new List<RuleActionParameter>
                    {
                        new RuleActionParameter { ParamKey = "FieldName", ParamValue = "Talep.Buyukluk" },
                        new RuleActionParameter { ParamKey = "UpdateType", ParamValue = "CALCULATE" },
                        new RuleActionParameter { ParamKey = "Value", ParamValue = "EFFORT_TO_SIZE" }
                    }
                }
            };

            return new RuleModel
            {
                RuleName = ruleName,
                Expression = expression,
                AppliesTo = appliesTo,
                RuleSet = ruleSet,
                Priority = priority,
                IsActive = true,
                Actions = actions
            };
        }

        /// <summary>
        /// Manuel review gerektiren rule'lar için placeholder oluşturur
        /// </summary>
        private RuleModel CreateManualReviewRule(string ruleName, string appliesTo, string ruleSet, int priority, string originalCode)
        {
            return new RuleModel
            {
                RuleName = $"{ruleName}_ManualReview",
                Expression = "1 == 1", // TODO: Manual review required
                AppliesTo = appliesTo,
                RuleSet = ruleSet,
                Priority = priority,
                IsActive = false, // Disabled until manual review
                Actions = new List<RuleAction>
                {
                    new RuleAction
                    {
                        ActionName = "AddComment",
                        ConditionType = "Success",
                        ExecutionOrder = 1,
                        Parameters = new List<RuleActionParameter>
                        {
                            new RuleActionParameter { ParamKey = "CommentText", ParamValue = $"MANUAL REVIEW REQUIRED - Original XML code:\n{originalCode}" }
                        }
                    }
                }
            };
        }

        /// <summary>
        /// C# condition'ını boolean expression'a çevirir
        /// </summary>
        private string ConvertConditionToExpression(string condition)
        {
            // Cast'leri kaldır (örn: (string), (int), (double))
            condition = Regex.Replace(condition, @"\(\s*(string|int|double|float|decimal|bool)\s*\)", "");

            // self["FieldName"] → body.Fields.FieldName
            condition = Regex.Replace(condition, @"self\[""([^""]+)""\]", match =>
            {
                var fieldName = match.Groups[1].Value.Replace(".", "_");
                return $"body.Fields.{fieldName}";
            });

            // self.Fields["FieldName"].Value → body.Fields.FieldName  
            condition = Regex.Replace(condition, @"self\.Fields\[""([^""]+)""\]\.Value", match =>
            {
                var fieldName = match.Groups[1].Value.Replace(".", "_");
                return $"body.Fields.{fieldName}";
            });

            // string.IsNullOrEmpty() → string.IsNullOrWhiteSpace()
            condition = Regex.Replace(condition, @"string\.IsNullOrEmpty", "string.IsNullOrWhiteSpace");

            return condition.Trim();
        }

        /// <summary>
        /// Value'yu literal string'e çevirir
        /// </summary>
        private string ConvertValueToLiteral(string value)
        {
            // self.Fields["CreatedBy"].Value → {CreatedBy} placeholder
            if (value.Contains("self.Fields[") && value.Contains("].Value"))
            {
                var match = Regex.Match(value, @"self\.Fields\[""([^""]+)""\]\.Value");
                if (match.Success)
                {
                    return $"{{{match.Groups[1].Value}}}"; // Placeholder for dynamic value
                }
            }

            // String literal
            if (value.StartsWith("\"") && value.EndsWith("\""))
            {
                return value.Substring(1, value.Length - 2);
            }

            return value;
        }

        private bool HasComplexLogic(string code) => 
            code.Contains("HasParent()") || 
            code.Contains("Parent.") || 
            code.Contains("DateTime.Now") ||
            code.Contains("Contains(") ||
            code.Contains("string[]") ||
            code.Contains("for (") ||
            code.Contains("while (");

        private bool HasCalculationLogic(string code) => 
            code.Contains("double ") || 
            code.Contains("int ") || 
            code.Contains("efor") ||
            code.Contains("<=") || 
            code.Contains(">=") ||
            (code.Contains("if") && (code.Contains("efor") || code.Contains("Effort")));

        private List<string> ExtractCalculationFields(string code)
        {
            var fields = new List<string>();
            
            // Extract effort-related fields
            if (code.Contains("Effort") || code.Contains("efor"))
            {
                fields.Add("Microsoft.VSTS.Scheduling.Effort");
            }
            
            if (code.Contains("Buyukluk"))
            {
                fields.Add("Talep.Buyukluk");
            }

            return fields;
        }
    }

    #region Helper Classes

    public class ConversionResult
    {
        public List<RuleModel> ConvertedRules { get; set; } = new List<RuleModel>();
        public List<string> ConversionNotes { get; set; } = new List<string>();
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public class SingleRuleConversionResult
    {
        public List<RuleModel> Rules { get; set; } = new List<RuleModel>();
        public List<string> Notes { get; set; } = new List<string>();
    }

    public class CodeAnalysis
    {
        public List<string> IfConditions { get; set; } = new List<string>();
        public List<FieldAssignment> FieldAssignments { get; set; } = new List<FieldAssignment>();
        public List<StateTransition> StateTransitions { get; set; } = new List<StateTransition>();
        public List<string> CalculationFields { get; set; } = new List<string>();
        public bool HasSimpleCondition { get; set; }
        public bool HasCalculation { get; set; }
    }

    public class FieldAssignment
    {
        public string FieldName { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public class StateTransition
    {
        public string NewState { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
    }

    #endregion
} 