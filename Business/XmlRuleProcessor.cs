using BurganAzureDevopsAggregator.Models;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace BurganAzureDevopsAggregator.Business
{
    public class XmlRuleProcessor
    {
        private readonly ILogger<XmlRuleProcessor> _logger;
        private readonly AzureDevOpsClient _azureDevOpsClient;

        public XmlRuleProcessor(ILogger<XmlRuleProcessor> logger, AzureDevOpsClient azureDevOpsClient)
        {
            _logger = logger;
            _azureDevOpsClient = azureDevOpsClient;
        }

        /// <summary>
        /// XML calculation rule'ı execute eder
        /// </summary>
        public async Task<bool> ExecuteXmlCalculationRuleAsync(XmlCalculationRule xmlRule, FlatWorkItemModel workItem)
        {
            try
            {
                // XML rule code'unu mevcut sistem format'ına adapt et
                var adaptedCode = AdaptXmlRuleCode(xmlRule.CSharpCode);
                
                // Field accessor wrapper oluştur (missing field'lar için güvenli)
                var fieldAccessor = new FieldAccessorWrapper(workItem.Fields);
                
                _logger.LogDebug($"🔧 Executing XML rule '{xmlRule.Name}' with adapted code");
                _logger.LogDebug($"Adapted code: {adaptedCode}");
                
                // C# script'i execute et
                var script = CSharpScript.Create(adaptedCode, 
                    options: ScriptOptions.Default
                        .WithReferences(
                            typeof(object).Assembly,                           // System.Object
                            typeof(System.Linq.Enumerable).Assembly,          // System.Linq
                            typeof(System.Text.RegularExpressions.Regex).Assembly, // System.Text.RegularExpressions
                            typeof(System.Console).Assembly,                   // System.Console
                            typeof(System.Collections.Generic.List<>).Assembly, // System.Collections.Generic
                            typeof(System.DateTime).Assembly,                  // System.DateTime
                            typeof(System.Math).Assembly,                      // System.Math
                            typeof(System.Convert).Assembly                    // System.Convert
                        )
                        .WithImports(
                            "System", 
                            "System.Linq", 
                            "System.Text.RegularExpressions", 
                            "System.Collections.Generic",
                            "System.Globalization"
                        ),
                    globalsType: typeof(FieldAccessorWrapper));

                var result = await script.RunAsync(fieldAccessor);

                // Field değişiklikleri varsa Azure DevOps'a gönder
                if (fieldAccessor.HasChanges)
                {
                    await UpdateWorkItemFieldsAsync(workItem, fieldAccessor.GetChanges());
                    _logger.LogInformation($"📝 XML rule '{xmlRule.Name}' updated {fieldAccessor.GetChanges().Count} fields");
                }
                else
                {
                    _logger.LogDebug($"📝 XML rule '{xmlRule.Name}' completed with no field changes");
                }

                _logger.LogInformation($"✅ XML Rule '{xmlRule.Name}' executed successfully");
                return true;
            }
            catch (Microsoft.CodeAnalysis.Scripting.CompilationErrorException compilationEx)
            {
                _logger.LogError(compilationEx, $"❌ XML Rule '{xmlRule.Name}' compilation failed. Errors: {string.Join(", ", compilationEx.Diagnostics.Select(d => d.ToString()))}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ XML Rule '{xmlRule.Name}' execution failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// XML rule code'unu mevcut format'a adapt eder
        /// </summary>
        private string AdaptXmlRuleCode(string xmlCode)
        {
            // self["field"] → GetField("field") / SetField("field", value)
            string adaptedCode = xmlCode;

            // Read operations: self["field"] → GetField("field")
            adaptedCode = Regex.Replace(adaptedCode, 
                @"\(string\)self\[""([^""]+)""\]", 
                @"GetField(""$1"")", 
                RegexOptions.IgnoreCase);

            adaptedCode = Regex.Replace(adaptedCode, 
                @"self\[""([^""]+)""\](?!\s*=)", 
                @"GetField(""$1"")", 
                RegexOptions.IgnoreCase);

            // Write operations: self["field"] = value → SetField("field", value)
            adaptedCode = Regex.Replace(adaptedCode, 
                @"self\[""([^""]+)""\]\s*=\s*([^;]+);", 
                @"SetField(""$1"", $2);", 
                RegexOptions.IgnoreCase);

            return adaptedCode;
        }

        /// <summary>
        /// WorkItem field'larını Azure DevOps'ta günceller
        /// </summary>
        private async Task UpdateWorkItemFieldsAsync(FlatWorkItemModel workItem, Dictionary<string, string> changes)
        {
            if (!changes.Any()) return;

            try
            {
                var projectName = workItem.Fields.ContainsKey("System_TeamProject") 
                    ? workItem.Fields["System_TeamProject"]?.ToString() 
                    : "DefaultProject";

                await _azureDevOpsClient.UpdateWorkItemFieldAsync(workItem.WorkItemId, changes, projectName);
                
                _logger.LogInformation($"📝 Updated {changes.Count} fields for WorkItem {workItem.WorkItemId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Failed to update WorkItem {workItem.WorkItemId} fields");
                throw;
            }
        }
    }

    /// <summary>
    /// XML calculation rule model
    /// </summary>
    public class XmlCalculationRule
    {
        public string Name { get; set; }
        public string AppliesTo { get; set; }
        public string CSharpCode { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// Field accessor wrapper for XML rules
    /// </summary>
    public class FieldAccessorWrapper
    {
        private readonly IDictionary<string, object> _fields;
        private readonly Dictionary<string, string> _changes;

        public FieldAccessorWrapper(IDictionary<string, object> fields)
        {
            _fields = fields;
            _changes = new Dictionary<string, string>();
        }

        public string GetField(string fieldName)
        {
            var key = fieldName.Replace(".", "_");
            
            if (_fields.ContainsKey(key))
            {
                return _fields[key]?.ToString() ?? string.Empty;
            }
            
            // Missing field için null/empty döndür (XML rule'lar genelde string bekler)
            return string.Empty;
        }

        public void SetField(string fieldName, object value)
        {
            var key = fieldName.Replace(".", "_");
            var stringValue = value?.ToString() ?? string.Empty;
            
            // Azure DevOps için orijinal field name (nokta ile)
            var azureFieldName = fieldName.Contains("_") ? fieldName.Replace("_", ".") : fieldName;
            
            _changes[azureFieldName] = stringValue; 
            _fields[key] = stringValue; // Internal access için
        }

        public bool HasChanges => _changes.Any();
        public Dictionary<string, string> GetChanges() => new Dictionary<string, string>(_changes);
    }
} 