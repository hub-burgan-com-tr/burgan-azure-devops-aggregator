using RulesEngine.Models;
using RulesEngine;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BurganAzureDevopsAggregator.Models;

public class RuleValidator
{
    public async Task<(bool isValid, string error)> ValidateExpressionAsync(string expression)
    {
        var testRule = new Rule
        {
            RuleName = "TestRule",
            Expression = expression
        };

        var workflow = new Workflow
        {
            WorkflowName = "ValidationWorkflow",
            Rules = new List<Rule> { testRule }
        };

        var settings = new ReSettings
        {
            CustomTypes = new[] { typeof(FlatWorkItemModel) }
        };

        var rulesEngine = new RulesEngine.RulesEngine(new[] { workflow }, settings);


        try
        {
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
