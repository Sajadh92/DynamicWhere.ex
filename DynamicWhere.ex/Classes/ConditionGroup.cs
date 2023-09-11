namespace DynamicWhere.ex;

public class ConditionGroup
{
    public int Sort { get; set; }
    public Connector Connector { get; set; }
    public List<Condition> Conditions { get; set; } = new();
    public List<ConditionGroup> SubConditionGroups { get; set; } = new();
}