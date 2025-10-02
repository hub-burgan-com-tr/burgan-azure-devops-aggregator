using BurganAzureDevopsAggregator.Business;
using BurganAzureDevopsAggregator.Models;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace BurganAzureDevopsAggregator.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RulesController : ControllerBase
    {
        private readonly XmlToJsonConverter _xmlToJsonConverter;
        private readonly ILogger<RulesController> _logger;

        public RulesController(XmlToJsonConverter xmlToJsonConverter, ILogger<RulesController> logger)
        {
            _xmlToJsonConverter = xmlToJsonConverter;
            _logger = logger;
        }

        /// <summary>
        /// Test endpoint to verify controller is working
        /// </summary>
        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { Message = "RulesController is working", Controller = "Rules", Routes = new[] { "POST convert-xml-to-json" } });
        }

        /// <summary>
        /// XML rule'ları JSON formatına dönüştürür
        /// </summary>
        [HttpPost("convert-xml-to-json")]
        public IActionResult ConvertXmlToJson([FromBody] XmlConvertRequest request)
        {
            try
            {
                _logger.LogInformation("🔄 XML to JSON conversion requested via /api/rules/convert-xml-to-json");
                
                // Model validation kontrolü
                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("Model validation failed: {Errors}", ModelState);
                    return BadRequest(ModelState);
                }

                if (request == null)
                {
                    _logger.LogWarning("Request is null");
                    return BadRequest("Request body is required");
                }

                if (string.IsNullOrWhiteSpace(request.XmlContent))
                {
                    _logger.LogWarning("XmlContent is null or empty");
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
    public class XmlConvertRequest
    {
        [Required(ErrorMessage = "XML content is required")]
        public string XmlContent { get; set; } = string.Empty;
        public string? RuleSet { get; set; }
        public int? Priority { get; set; }
    }
} 