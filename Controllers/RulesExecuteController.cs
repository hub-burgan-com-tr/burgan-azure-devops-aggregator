using BurganAzureDevopsAggregator.Business;
using BurganAzureDevopsAggregator.Models;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RulesEngine.Models;
using System.Text.Json;
using BurganAzureDevopsAggregator.Helpers;
using System.Dynamic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace BurganAzureDevopsAggregator.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RulesExecuteController : ControllerBase
    {
        private readonly RulesEngineService _rulesEngineService;
        private readonly ILogger<RulesExecuteController> _logger;
        private readonly RulesHelper _rulesHelper;
        private readonly RulesService _rulesService;
        private readonly FieldProcessingService _fieldProcessingService;
        private readonly XmlRuleImportService _xmlRuleImportService;
        private readonly XmlToJsonConverter _xmlToJsonConverter;
        
        // Webhook rate limiting için
        private static readonly Dictionary<int, DateTime> _lastWebhookTimes = new();
        private readonly TimeSpan MinWebhookInterval = TimeSpan.FromSeconds(5); // 5 saniye minimum interval


        public RulesExecuteController(
            RulesEngineService rulesEngineService, 
            ILogger<RulesExecuteController> logger,
            RulesHelper rulesHelper, 
            RulesService rulesService,
            FieldProcessingService fieldProcessingService,
            XmlRuleImportService xmlRuleImportService,
            XmlToJsonConverter xmlToJsonConverter)
        {
            _rulesEngineService = rulesEngineService;
            _logger = logger;
            _rulesHelper = rulesHelper;
            _rulesService = rulesService;
            _fieldProcessingService = fieldProcessingService;
            _xmlRuleImportService = xmlRuleImportService;
            _xmlToJsonConverter = xmlToJsonConverter;
        }
        [HttpGet("{ruleset}")]
        public async Task<IActionResult> GetRules(string ruleset)
        {
            var rules = await _rulesService.GetActiveRulesWithActionsAsync(ruleset);
            return Ok(rules);
        }
        private ExpandoObject ConvertToExpando(Dictionary<string, object> dictionary)
        {
            IDictionary<string, object> expando = new ExpandoObject();
            foreach (var pair in dictionary)
            {
                expando[pair.Key] = pair.Value;
            }

            return (ExpandoObject)expando;
        }

        [HttpPost("execute")]
        public async Task<IActionResult> Post([FromBody] WorkItemModel payload)
        {
            try
            {
                // Webhook rate limiting - aynı WorkItem için çok sık request'leri engelle
                var workItemId = payload?.Resource?.WorkItemId ?? 0;
                if (workItemId > 0 && _lastWebhookTimes.ContainsKey(workItemId))
                {
                    var timeSinceLastWebhook = DateTime.UtcNow - _lastWebhookTimes[workItemId];
                    if (timeSinceLastWebhook < MinWebhookInterval)
                    {
                        _logger.LogWarning($"🛑 Webhook rate limit: WorkItem {workItemId} blocked - last webhook {timeSinceLastWebhook.TotalSeconds:F1}s ago (min: {MinWebhookInterval.TotalSeconds}s)");
                        return Ok(new { Message = "Request rate limited", WorkItemId = workItemId });
                    }
                }
                
                // Update webhook timing
                if (workItemId > 0)
                {
                    _lastWebhookTimes[workItemId] = DateTime.UtcNow;
                }
                
                // Field processing ve validation'ı service'e delege et
                var (flatModel, bodyObject) = await _fieldProcessingService.ProcessWorkItemAsync(payload);
                
                // RulesEngine için parameter hazırla
                var ruleParameter = new RuleParameter("body", bodyObject);
                
                // Rule'ları çalıştır
                await _rulesEngineService.ExecuteRules(new List<RuleParameter> { ruleParameter }, flatModel);

                return Ok(new
                {
                    Message = "Rules executed successfully.",
                    WorkItemId = payload.Resource.WorkItemId,
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning($"Invalid payload: {ex.Message}");
                return BadRequest($"Invalid payload: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in RulesExecuteController while executing rules.");
                return StatusCode(500, $"An error occurred while executing rules: {ex.Message}");
            }
        }
        [HttpPost("save")]
        public async Task<IActionResult> SaveRules([FromBody] List<RuleModel> rules)
        {
            if (rules == null || !rules.Any())
                return BadRequest("Boş kural listesi.");

            try
            {
                int insertedCount = 0;
                int updatedCount = 0;

                foreach (var rule in rules)
                {
                    // Mevcut rule'ı bul
                    var existingRule = await _rulesService.FindExistingRuleAsync(
                        rule.RuleName, 
                        rule.RuleSet);

                    if (existingRule != null)
                    {
                        // Mevcut rule'ı güncelle
                        await _rulesService.UpdateRuleAsync(existingRule, rule);
                        updatedCount++;
                        _logger.LogInformation($"🔄 Updated existing rule: {rule.RuleName}");
                    }
                    else
                    {
                        // Yeni rule ekle
                        await _rulesService.SaveRulesAsync(new List<RuleModel> { rule });
                        insertedCount++;
                        _logger.LogInformation($"➕ Inserted new rule: {rule.RuleName}");
                    }
                }

                var message = $"Kurallar başarıyla işlendi. {insertedCount} yeni, {updatedCount} güncellendi.";
                _logger.LogInformation($"📊 Rule save summary: {insertedCount} inserted, {updatedCount} updated");
                
                return Ok(new { 
                    Message = message, 
                    TotalCount = rules.Count,
                    InsertedCount = insertedCount,
                    UpdatedCount = updatedCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving rules");
                return StatusCode(500, $"Kurallar kaydedilirken hata oluştu: {ex.Message}");
            }
        }

        [HttpPost("import-xml")]
        public async Task<IActionResult> ImportXmlRules([FromBody] XmlImportRequest request)
        {
            try
            {
                string xmlContent = null;

                // xmlContent veya xmlLines'dan birisi olmalı
                if (!string.IsNullOrWhiteSpace(request.XmlContent))
                {
                    xmlContent = request.XmlContent;
                }
                else if (request.XmlLines != null && request.XmlLines.Any())
                {
                    // xmlLines array'ini tek string'e çevir
                    xmlContent = string.Join(Environment.NewLine, request.XmlLines);
                }

                if (string.IsNullOrWhiteSpace(xmlContent))
                {
                    return BadRequest("xmlContent veya xmlLines belirtilmelidir.");
                }

                var result = await _xmlRuleImportService.ImportXmlRulesAsync(
                    xmlContent, 
                    request.RuleSet ?? "ImportedXmlRules",
                    request.Priority);

                var insertedCount = result.ProcessedRules.Count(r => r.Status == "Inserted");
                var updatedCount = result.ProcessedRules.Count(r => r.Status == "Updated");
                
                return Ok(new
                {
                    Message = $"XML import tamamlandı. {insertedCount} yeni kural eklendi, {updatedCount} mevcut kural güncellendi.",
                    InsertedCount = insertedCount,
                    UpdatedCount = updatedCount,
                    TotalProcessed = result.ProcessedRules.Count,
                    IsSuccess = result.IsSuccess,
                    Details = result.ProcessedRules.Select(r => new
                    {
                        r.Name,
                        r.Status,
                        r.Type,
                        r.Error
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "XML rule import failed");
                return StatusCode(500, $"XML import sırasında hata oluştu: {ex.Message}");
            }
        }

        /// <summary>
        /// XML rule'ları boolean expression + actions formatına dönüştürür
        /// </summary>
        [HttpPost("convert-xml-to-json")]
        public IActionResult ConvertXmlToJson([FromBody] XmlConvertRequestModel request)
        {
            try
            {
                _logger.LogInformation("🔄 XML to JSON conversion requested");

                if (string.IsNullOrWhiteSpace(request.XmlContent))
                {
                    return BadRequest("XML content is required");
                }

                var result = _xmlToJsonConverter.ConvertXmlToRules(
                    request.XmlContent, 
                    request.RuleSet ?? "ConvertedRules", 
                    request.Priority ?? 100
                );

                if (result.IsSuccess)
                {
                    _logger.LogInformation($"✅ XML conversion successful: {result.ConvertedRules.Count} rules converted");
                    
                    return Ok(new
                    {
                        Message = $"XML conversion completed: {result.ConvertedRules.Count} rules converted",
                        ConvertedRules = result.ConvertedRules,
                        ConversionNotes = result.ConversionNotes,
                        Summary = new
                        {
                            TotalRules = result.ConvertedRules.Count,
                            BooleanExpressionRules = result.ConvertedRules.Count(r => r.IsActive && !r.RuleName.Contains("ManualReview")),
                            ManualReviewRules = result.ConvertedRules.Count(r => r.RuleName.Contains("ManualReview")),
                            CalculationRules = result.ConvertedRules.Count(r => r.Actions.Any(a => a.ActionName == "UpdateField"))
                        }
                    });
                }
                else
                {
                    _logger.LogError($"❌ XML conversion failed: {result.ErrorMessage}");
                    return BadRequest(new
                    {
                        Message = "XML conversion failed",
                        Error = result.ErrorMessage,
                        ConversionNotes = result.ConversionNotes
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "XML to JSON conversion failed");
                return StatusCode(500, $"Conversion sırasında hata oluştu: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// XML conversion request model
    /// </summary>
    public class XmlConvertRequestModel
    {
        public string XmlContent { get; set; } = string.Empty;
        public string? RuleSet { get; set; }
        public int? Priority { get; set; }
    }
}