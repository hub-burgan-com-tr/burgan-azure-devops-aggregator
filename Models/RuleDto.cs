using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BurganAzureDevopsAggregator.Models
{
public class RuleDto
{
    public string RuleName { get; set; }
    public string Expression { get; set; }
    public string Action { get; set; }
    public string RuleSet { get; set; }
    public bool IsActive { get; set; }
    
    // Rule çalışma sırasını kontrol etmek için Priority eklendi (düşük değer = yüksek öncelik)
    public int Priority { get; set; } = 100;
    
    public List<ActionDto> Actions { get; set; }
}

public class ActionDto
{
    public string ActionName { get; set; }
    public List<ParameterDto> Parameters { get; set; }
}

public class ParameterDto
{
    public string ParamKey { get; set; }
    public string ParamValue { get; set; }
}

    }
