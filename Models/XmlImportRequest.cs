namespace BurganAzureDevopsAggregator.Models
{
    /// <summary>
    /// XML import request model
    /// </summary>
    public class XmlImportRequest
    {
        public string? XmlContent { get; set; }
        public string[]? XmlLines { get; set; }  
        public string? RuleSet { get; set; }
        public int? Priority { get; set; } 
} 
}