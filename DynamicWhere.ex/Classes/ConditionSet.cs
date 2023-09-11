namespace DynamicWhere.ex;

public class ConditionSet
{
    public int Sort { get; set; }
    public Intersection? Intersection { get; set; }
    public ConditionGroup ConditionGroup { get; set; } = new();
}