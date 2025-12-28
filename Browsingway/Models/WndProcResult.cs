namespace Browsingway.Models;

/// <summary>
/// Result from WndProc message handling.
/// </summary>
/// <param name="Handled">True if the message was handled and should not propagate.</param>
/// <param name="ReturnValue">The return value if handled.</param>
public readonly record struct WndProcResult(bool Handled, long ReturnValue)
{
	public static WndProcResult NotHandled => new(false, 0);
	public static WndProcResult HandledWith(long returnValue = 0) => new(true, returnValue);
}
