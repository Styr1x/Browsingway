using System.Runtime.InteropServices;

namespace Browsingway.Interop;

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

internal static class WndProcHandler
{
	public delegate long WndProcDelegate(IntPtr hWnd, uint msg, ulong wParam, long lParam);
	public delegate WndProcResult WndProcMessageDelegate(WindowsMessage msg, ulong wParam, long lParam);

	private static WndProcDelegate? _wndProcDelegate;
	private static IntPtr _hWnd;
	private static IntPtr _oldWndProcPtr;
	private static IntPtr _detourPtr;

	public static event WndProcMessageDelegate? WndProcMessage;

	public static void Initialise(IntPtr hWnd)
	{
		_hWnd = hWnd;

		_wndProcDelegate = WndProcDetour;
		_detourPtr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
		_oldWndProcPtr = NativeMethods.SetWindowLongPtr(hWnd, WindowLongType.GWL_WNDPROC, _detourPtr);
	}

	public static void Shutdown()
	{
		// If the current pointer doesn't match our detour, something swapped the pointer out from under us -
		// likely the InterfaceManager doing its own cleanup. Don't reset in that case, we'll trust the cleanup
		// is accurate.
		IntPtr curWndProcPtr = NativeMethods.GetWindowLongPtr(_hWnd, WindowLongType.GWL_WNDPROC);
		if (_oldWndProcPtr != IntPtr.Zero && curWndProcPtr == _detourPtr)
		{
			NativeMethods.SetWindowLongPtr(_hWnd, WindowLongType.GWL_WNDPROC, _oldWndProcPtr);
			_oldWndProcPtr = IntPtr.Zero;
		}
	}

	private static long WndProcDetour(IntPtr hWnd, uint msg, ulong wParam, long lParam)
	{
		// Ignore things not targeting the current window handle
		if (hWnd == _hWnd)
		{
			WndProcResult? result = WndProcMessage?.Invoke((WindowsMessage)msg, wParam, lParam);

			if (result is { Handled: true })
			{
				return result.Value.ReturnValue;
			}
		}

		return NativeMethods.CallWindowProc(_oldWndProcPtr, hWnd, msg, wParam, lParam);
	}
}
