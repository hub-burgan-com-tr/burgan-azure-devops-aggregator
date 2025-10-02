using BurganAzureDevopsAggregator.Models;
using BurganAzureDevopsAggregator.Helpers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace BurganAzureDevopsAggregator.Business
{
    public class FieldProcessingService
    {
        private readonly RulesService _rulesService;
        private readonly RulesHelper _rulesHelper;
        private readonly ILogger<FieldProcessingService> _logger;

        public FieldProcessingService(
            RulesService rulesService, 
            RulesHelper rulesHelper, 
            ILogger<FieldProcessingService> logger)
        {
            _rulesService = rulesService;
            _rulesHelper = rulesHelper;
            _logger = logger;
        }

        /// <summary>
        /// WorkItemModel'i FlatWorkItemModel'e dönüştürür ve missing field'ları handle eder
        /// </summary>
        public async Task<(FlatWorkItemModel flatModel, dynamic bodyObject)> ProcessWorkItemAsync(WorkItemModel payload)
        {
            if (payload?.Resource?.Revision?.Fields == null)
            {
                throw new ArgumentException("Invalid payload: Missing or empty 'Fields'.");
            }

            // Field mapping
            var ruleInputs = payload.Resource.Revision.Fields
                .ToDictionary(
                    field => field.Key.Replace(".", "_"),
                    field => field.Value is JsonElement jsonElem
                        ? _rulesHelper.ExtractJsonValue(jsonElem)
                        : field.Value
                );

            if (!ruleInputs.Any())
            {
                throw new ArgumentException("Invalid payload: 'Fields' contains no valid data.");
            }

            // FlatWorkItemModel oluştur
            var flatModel = new FlatWorkItemModel
            {
                Fields = ruleInputs,
                WorkItemId = payload.Resource.WorkItemId,
                isResultCode = "10"
            };

            // JSON dönüşümü
            var jsonObject = JsonConvert.DeserializeObject<JObject>(JsonSerializer.Serialize(flatModel));
            var bodyParamList = MapHelper.ToDictionary(jsonObject);
            dynamic bodyExpando = MapHelper.ToExpandoObject(bodyParamList);

            // Missing field'ları handle et
            await EnsureMissingFieldsAsNullAsync(bodyExpando, flatModel);

            return (flatModel, bodyExpando);
        }

        /// <summary>
        /// Rule expression'larından field'ları extract ederek missing field'ları null olarak ekler
        /// </summary>
        private async Task EnsureMissingFieldsAsNullAsync(dynamic bodyExpando, FlatWorkItemModel flatModel)
        {
            if (bodyExpando?.Fields == null) return;

            var fieldsDict = (IDictionary<string, object>)bodyExpando.Fields;
            
            // Project name'i al
            var projectName = fieldsDict.ContainsKey("System_TeamProject")
                ? fieldsDict["System_TeamProject"]?.ToString()
                : string.Empty;

            // Rule'lardan field'ları extract et
            var extractedFields = await ExtractFieldsFromRulesAsync(projectName);

            // Missing field'ları null olarak ekle
            var addedCount = 0;
            foreach (var fieldName in extractedFields)
            {
                if (!fieldsDict.ContainsKey(fieldName))
                {
                    fieldsDict[fieldName] = null;
                    addedCount++;
                }
            }
            
            if (addedCount > 0)
            {
                _logger.LogInformation($"🔧 Added {addedCount} missing fields as null for rule evaluation");
            }
        }

        /// <summary>
        /// Aktif rule'ların expression'larından field'ları extract eder
        /// </summary>
        private async Task<HashSet<string>> ExtractFieldsFromRulesAsync(string projectName)
        {
            var extractedFields = new HashSet<string>();
            
            try 
            {
                var dbRules = await _rulesService.GetActiveRulesWithActionsAsync(projectName);
                foreach (var rule in dbRules)
                {
                    if (!string.IsNullOrEmpty(rule.Expression))
                    {
                        // body.Fields.FieldName pattern'ını extract et
                        var matches = Regex.Matches(
                            rule.Expression, 
                            @"body\.Fields\.([a-zA-Z_][a-zA-Z0-9_]*)", 
                            RegexOptions.IgnoreCase
                        );
                        
                        foreach (Match match in matches)
                        {
                            if (match.Groups.Count > 1)
                            {
                                extractedFields.Add(match.Groups[1].Value);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"⚠️ Rule field extraction failed: {ex.Message}");
                // Fallback: Yaygın field'ları ekle
                AddCommonMissingFields(extractedFields);
            }

            // Eğer hiç field extract edilemezse fallback kullan
            if (!extractedFields.Any())
            {
                AddCommonMissingFields(extractedFields);
            }

            return extractedFields;
        }

        /// <summary>
        /// Fallback için yaygın field'ları ekler
        /// </summary>
        private void AddCommonMissingFields(HashSet<string> extractedFields)
        {
            var commonFields = new[]
            {
                "Talep_finansalkazanimbvs",
                "Talep_musterimemnuniyetibvs", 
                "talep_firsatolusturma",
                "talep_finansalkazanimaciklamasi",
                "talep_musterimemnuniyetiaciklamasi",
                "talep_firsatolusturmaaciklamasi",
                "talep_stratejiveokraciklamasi",
                "System_Tags",
                "Microsoft_VSTS_Common_Priority",
                "Microsoft_VSTS_Common_Severity"
            };

            foreach (var field in commonFields)
            {
                extractedFields.Add(field);
            }
        }
    }
} 