using BurganAzureDevopsAggregator.Models;
using Microsoft.Extensions.Logging;
using System.Xml.Linq;

namespace BurganAzureDevopsAggregator.Business
{
    public class XmlRuleImportService
    {
        private readonly ILogger<XmlRuleImportService> _logger;
        private readonly RulesService _rulesService;

        public XmlRuleImportService(ILogger<XmlRuleImportService> logger, RulesService rulesService)
        {
            _logger = logger;
            _rulesService = rulesService;
        }

        /// <summary>
        /// XML string'den rule'ları parse eder ve database'e import eder
        /// </summary>
        public async Task<ImportResult> ImportXmlRulesAsync(string xmlContent, string ruleSet = "ImportedRules", int? priority = null)
        {
            var result = new ImportResult();
            
            try
            {
                var xmlRules = ParseXmlRules(xmlContent);
                
                foreach (var xmlRule in xmlRules)
                {
                    try
                    {
                        // XML calculation rule'ı database rule format'ına convert et
                        var databaseRule = ConvertXmlRuleToDatabase(xmlRule, ruleSet, priority);
                        
                        result.ProcessedRules.Add(new ProcessedRule 
                        { 
                            Name = xmlRule.Name, 
                            Status = "Converted", 
                            Type = "XmlCalculation",
                            DatabaseRule = databaseRule
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to convert XML rule '{xmlRule.Name}': {ex.Message}");
                        result.ProcessedRules.Add(new ProcessedRule 
                        { 
                            Name = xmlRule.Name, 
                            Status = "Failed", 
                            Error = ex.Message 
                        });
                    }
                }

                // Başarılı rule'ları database'e kaydet veya güncelle (UPSERT)
                var successfulRules = result.ProcessedRules
                    .Where(r => r.Status == "Converted" && r.DatabaseRule != null)
                    .ToList();

                if (successfulRules.Any())
                {
                    int insertedCount = 0;
                    int updatedCount = 0;

                    foreach (var processedRule in successfulRules)
                    {
                        var newRule = processedRule.DatabaseRule;
                        
                        // Mevcut rule'ı bul
                        var existingRule = await _rulesService.FindExistingRuleAsync(
                            newRule.RuleName, 
                            newRule.RuleSet);

                        if (existingRule != null)
                        {
                            // Mevcut rule'ı güncelle
                            await _rulesService.UpdateRuleAsync(existingRule, newRule);
                            processedRule.Status = "Updated";
                            updatedCount++;
                            _logger.LogInformation($"🔄 Updated existing rule: {newRule.RuleName}");
                        }
                        else
                        {
                            // Yeni rule ekle
                            await _rulesService.SaveRulesAsync(new List<RuleModel> { newRule });
                            processedRule.Status = "Inserted";
                            insertedCount++;
                            _logger.LogInformation($"➕ Inserted new rule: {newRule.RuleName}");
                        }
                    }

                    result.ImportedCount = insertedCount + updatedCount;
                    _logger.LogInformation($"📊 Import summary: {insertedCount} inserted, {updatedCount} updated");
                }

                var priorityInfo = priority.HasValue ? $" with priority {priority.Value}" : " with default priority";
                var finalInsertedCount = result.ProcessedRules.Count(r => r.Status == "Inserted");
                var finalUpdatedCount = result.ProcessedRules.Count(r => r.Status == "Updated");
                
                _logger.LogInformation($"✅ XML Import completed{priorityInfo}: {finalInsertedCount} inserted, {finalUpdatedCount} updated, {result.ImportedCount} total processed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ XML Import failed: {ex.Message}");
                result.ProcessedRules.Add(new ProcessedRule 
                { 
                    Name = "XML_PARSE_ERROR", 
                    Status = "Failed", 
                    Error = ex.Message 
                });
            }

            return result;
        }

        /// <summary>
        /// XML content'i parse ederek XmlCalculationRule listesi döner
        /// </summary>
        public List<XmlCalculationRule> ParseXmlRules(string xmlContent)
        {
            var rules = new List<XmlCalculationRule>();

            try
            {
                // XML'i parse et
                var xmlDoc = XDocument.Parse(xmlContent);
                
                // Tek rule veya rules collection'ı handle et
                var ruleElements = xmlDoc.Descendants("rule");
                
                foreach (var ruleElement in ruleElements)
                {
                    var rule = new XmlCalculationRule
                    {
                        Name = ruleElement.Attribute("name")?.Value ?? "UnnamedRule",
                        AppliesTo = ruleElement.Attribute("appliesTo")?.Value ?? "All",
                        CSharpCode = ruleElement.Value.Trim(),
                        IsActive = true
                    };

                    rules.Add(rule);
                    _logger.LogDebug($"📄 Parsed XML rule: {rule.Name}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse XML rules");
                throw;
            }

            return rules;
        }

        /// <summary>
        /// XML rule'ı database RuleModel format'ına convert eder
        /// </summary>
        private RuleModel ConvertXmlRuleToDatabase(XmlCalculationRule xmlRule, string ruleSet, int? priority)
        {
            // XML calculation rule'ları için özel expression format
            var expression = $"XmlCalculationRule(\"{xmlRule.Name}\")";
            
            var databaseRule = new RuleModel
            {
                RuleName = xmlRule.Name,
                Expression = expression,
                AppliesTo = xmlRule.AppliesTo, // AppliesTo burada, parameter'da değil
                RuleSet = ruleSet,
                IsActive = xmlRule.IsActive,
                Priority = priority ?? 100, // Priority belirtilmemişse 100 kullan
                Actions = new List<RuleAction>
                {
                    new RuleAction
                    {
                        ActionName = "ExecuteXmlCalculation",
                        ConditionType = "Success",
                        Parameters = new List<RuleActionParameter>
                        {
                            new RuleActionParameter
                            {
                                ParamKey = "XmlRuleName",
                                ParamValue = xmlRule.Name
                            },
                            new RuleActionParameter
                            {
                                ParamKey = "CSharpCode",
                                ParamValue = xmlRule.CSharpCode
                            }
                            // AppliesTo parameter'ı kaldırıldı - RuleModel.AppliesTo kullanılacak
                        }
                    }
                }
            };

            return databaseRule;
        }
    }

    /// <summary>
    /// Import işlemi sonucu
    /// </summary>
    public class ImportResult
    {
        public List<ProcessedRule> ProcessedRules { get; set; } = new List<ProcessedRule>();
        public int ImportedCount { get; set; }
        public bool IsSuccess => ProcessedRules.All(r => r.Status == "Converted");
    }

    /// <summary>
    /// İşlenen rule bilgisi
    /// </summary>
    public class ProcessedRule
    {
        public string Name { get; set; }
        public string Status { get; set; } // "Converted", "Failed"
        public string Type { get; set; }
        public string Error { get; set; }
        public RuleModel DatabaseRule { get; set; }
    }
} 