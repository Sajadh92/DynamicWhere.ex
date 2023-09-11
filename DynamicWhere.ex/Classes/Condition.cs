namespace DynamicWhere.ex;

public class Condition
{
    public int Sort { get; set; }
    public string? Field { get; set; }
    public DataType DataType { get; set; }
    public Operator Operator { get; set; }
    public List<string> Values { get; set; } = new();
}