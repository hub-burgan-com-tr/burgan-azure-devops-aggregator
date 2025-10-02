using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Threading.Tasks;

namespace BurganAzureDevopsAggregator.Models
{

    public class FlatWorkItemModel
    {
        public Dictionary<string, object> Fields { get; set; }
        public int WorkItemId { get; set; }
        public string isResultCode { get; set; }
}


}

