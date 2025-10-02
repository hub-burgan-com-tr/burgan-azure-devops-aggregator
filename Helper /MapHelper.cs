using System.Dynamic;
using Newtonsoft.Json.Linq;

namespace BurganAzureDevopsAggregator.Helpers;

public static class MapHelper
{
    public static Dictionary<string, object> ToDictionary(JObject jObject)
    {
        //TODO: Tüm prop'lar ToLower ile eşit seviyeye getirildi. Etkisi test edilecek.
        var dictionary = new Dictionary<string, object>();

        foreach (var property in jObject.Properties())
        {
            var value = property.Value;
            
            switch (value.Type)
            {
                case JTokenType.Object:
                    dictionary[property.Name] = ToDictionary((JObject)value);
                    break;
                case JTokenType.Array:
                    dictionary[property.Name] = ToList((JArray)value);
                    break;
                default:
                    var jValue = (JValue)value;
                    if (jValue.Type == JTokenType.Integer)
                    {
                        dictionary[property.Name] = Convert.ToInt32(jValue.Value);
                    }
                    else if (jValue.Type == JTokenType.Float)
                    {
                        dictionary[property.Name] = Convert.ToDouble(jValue.Value);
                    }
                    else
                    {
                        dictionary[property.Name] = jValue.Value;
                    }
                    break;
            }
        }

        return dictionary;
    }
    
    public static List<object> ToList(JArray jArray)
    {
        var list = new List<object>();

        foreach (var item in jArray)
        {
            switch (item.Type)
            {
                case JTokenType.Object:
                    list.Add(ToDictionary((JObject)item));
                    break;
                case JTokenType.Array:
                    list.Add(ToList((JArray)item));
                    break;
                default:
                    var value = ((JValue)item).Value;
                    if (value != null) list.Add(value);
                    break;
            }
        }

        return list;
    }
    
    public static ExpandoObject ToExpandoObject(Dictionary<string, object> dictionary)
    {
        var expandoObject = new ExpandoObject() as IDictionary<string, object>;

        foreach (var kvp in dictionary)
        {
            if (kvp.Value is Dictionary<string, object> nestedDictionary)
            {
                expandoObject[kvp.Key] = ToExpandoObject(nestedDictionary);
            }
            else if (kvp.Value is List<object> nestedList)
            {
                expandoObject[kvp.Key] = ToExpandoList(nestedList);
            }
            else
            {
                expandoObject[kvp.Key] = kvp.Value;
            }
        }

        return (ExpandoObject)expandoObject!;
    }
    
    public static List<object> ToExpandoList(List<object> list)
    {
        var expandoList = new List<object>();

        foreach (var item in list)
        {
            if (item is Dictionary<string, object> nestedDictionary)
            {
                expandoList.Add(ToExpandoObject(nestedDictionary));
            }
            else if (item is List<object> nestedList)
            {
                expandoList.Add(ToExpandoList(nestedList));
            }
            else
            {
                expandoList.Add(item);
            }
        }

        return expandoList;
    }
    
}