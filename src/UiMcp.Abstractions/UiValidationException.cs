namespace UiMcp.Abstractions;

/// <summary>
/// Thrown when a UI tree is refused. Carrying its own type matters: the MCP layer must be able to
/// tell a DELIBERATE REFUSAL apart from an internal fault, and report the refusal verbatim to the
/// caller. Windows-mcp learned this - the SDK otherwise flattens every non-MCP exception into
/// "An error occurred invoking '&lt;tool&gt;'", which turns a precise, actionable "Note: unknown
/// prop 'onclick'" into an opaque shrug and hides the guard that just did its job.
/// </summary>
public sealed class UiValidationException : Exception
{
    public UiValidationException(string message) : base(message) { }

    public UiValidationException(string message, Exception inner) : base(message, inner) { }
}
