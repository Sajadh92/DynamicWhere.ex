namespace DynamicWhere.ex;

public class LogicException : Exception
{
    public LogicException(string message)
        : base(message)
    {
    }
}