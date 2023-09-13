namespace DynamicWhere.ex;

/// <summary>
/// Represents an exception that is thrown when a logic or validation error occurs in the application.
/// </summary>
public class LogicException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LogicException"/> class with the specified error message.
    /// </summary>
    /// <param name="message">The error message that describes the reason for the exception.</param>
    public LogicException(string message)
        : base(message)
    {
    }
}