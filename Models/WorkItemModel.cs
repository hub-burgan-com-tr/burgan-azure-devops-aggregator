public class WorkItemModel
{
    public string EventType { get; set; }
    public Resource Resource { get; set; }
}

public class Resource
{
    public int WorkItemId { get; set; }
    public Dictionary<string, FieldChange> Fields { get; set; }
    public Revision Revision { get; set; }
}

public class FieldChange
{
    public object? OldValue { get; set; }
    public object? NewValue { get; set; }
}

public class Revision
{
    public Dictionary<string, object> Fields { get; set; }
}
