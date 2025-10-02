using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace BurganAzureDevopsAggregator.Models
{
public class RuleModel
{
    public int? RuleId { get; set; }
    public string RuleName { get; set; }
    public string Expression { get; set; }
    public string? Action { get; set; }
    public string AppliesTo { get; set; }

    public string RuleSet { get; set; }
    public bool IsActive { get; set; }
    
    // Rule çalışma sırasını kontrol etmek için Priority eklendi (düşük değer = yüksek öncelik)
    public int Priority { get; set; } = 100;

    public List<RuleAction> Actions { get; set; }
}

public class RuleAction
{
    public int? ActionId { get; set; }
    public string ActionName { get; set; }
        public string ConditionType { get; set; }
    public int ExecutionOrder { get; set; } = 1;

    public int? RuleId { get; set; }

        [JsonIgnore]
        [ValidateNever] 
    public RuleModel Rule { get; set; }
    public List<RuleActionParameter> Parameters { get; set; }
}

public class RuleActionParameter
{
    public int? ParameterId { get; set; }
    public string ParamKey { get; set; }
    public string ParamValue { get; set; }
    public int? ActionId { get; set; }
        [JsonIgnore]
        [ValidateNever] 
    public RuleAction Action { get; set; }
}

}
