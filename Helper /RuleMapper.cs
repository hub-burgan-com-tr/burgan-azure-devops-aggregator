using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BurganAzureDevopsAggregator.Models;

namespace BurganAzureDevopsAggregator.Helper 
{
    public class RuleMapper
    {
        public static RuleModel MapToEntity(RuleDto dto)
    {
        return new RuleModel
        {
            RuleName = dto.RuleName,
            Expression = dto.Expression,
            Action = dto.Action,
            RuleSet = dto.RuleSet,
            IsActive = dto.IsActive,
            Actions = dto.Actions?.Select(a => new RuleAction
            {
                ActionName = a.ActionName,
                Parameters = a.Parameters?.Select(p => new RuleActionParameter
                {
                    ParamKey = p.ParamKey,
                    ParamValue = p.ParamValue
                }).ToList()
            }).ToList()
        };
    }
    }
}