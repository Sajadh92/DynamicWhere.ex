namespace DynamicWhere.ex.Exceptions;

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

    /// <summary>
    /// Initializes a new instance carrying what the code is about, where the code alone does not say.
    /// </summary>
    /// <param name="message">One of the stable strings on <c>ErrorCode</c>.</param>
    /// <param name="subject">The type, field or alias the refusal names.</param>
    public LogicException(string message, string? subject)
        : base(message)
    {
        Subject = subject;
    }

    /// <summary>
    /// What the refusal is about — a type name, a field path — or null where the code says it all.
    /// </summary>
    /// <remarks>
    /// Here rather than interpolated into the message, because the message is the machine-readable
    /// half: a caller matches on it, and hosts put it straight into an error envelope's code. A code
    /// that carried a type name would be a different string on every type.
    /// </remarks>
    public string? Subject { get; }
}