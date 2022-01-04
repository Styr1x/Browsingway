using System.Runtime.InteropServices;

namespace Browsingway;

internal class WndProcHandler
{
	public delegate long WndProcDelegate(IntPtr hWnd, uint msg, ulong wParam, long lParam);

	public delegate (bool, long) WndProcMessageDelegate(WindowsMessage msg, ulong wParam, long lParam);

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
			(bool, long)? resp = WndProcMessage?.Invoke((WindowsMessage)msg, wParam, lParam);

			// Item1 is a bool, where true == capture event. If false, we're falling through default handling.
			if (resp is { Item1: true })
			{
				return resp.Value.Item2;
			}
		}

		return NativeMethods.CallWindowProc(_oldWndProcPtr, hWnd, msg, wParam, lParam);
	}
}