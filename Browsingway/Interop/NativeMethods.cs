using System.Runtime.InteropServices;

namespace Browsingway.Interop;

// Enums are not comprehensive for the sake of omitting stuff I won't use.
internal enum WindowLongType
{
	GWL_WNDPROC = -4
}

internal enum WindowsMessage
{
	WM_KEYDOWN = 0x0100,
	WM_KEYUP = 0x0101,
	WM_CHAR = 0x0102,
	WM_SYSKEYDOWN = 0x0104,
	WM_SYSKEYUP = 0x0105,
	WM_SYSCHAR = 0x0106,

	WM_LBUTTONDOWN = 0x0201
}

internal enum VirtualKey
{
	Shift = 0x10,
	Control = 0x11
}

internal static partial class NativeMethods
{
	public static bool IsKeyActive(VirtualKey key) => (GetKeyState((int)key) & 1) == 1;

	[LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
	public static partial IntPtr GetWindowLongPtr(IntPtr hWnd, WindowLongType nIndex);

	[LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
	public static partial IntPtr SetWindowLongPtr(IntPtr hWnd, WindowLongType nIndex, IntPtr dwNewLong);

	[LibraryImport("user32.dll", EntryPoint = "CallWindowProcW")]
	public static partial long CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, ulong wParam, long lParam);

	[LibraryImport("user32.dll")]
	private static partial short GetKeyState(int nVirtKey);
}
