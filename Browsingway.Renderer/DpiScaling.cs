using CefSharp.Structs;
using System.Runtime.InteropServices;

namespace Browsingway.Renderer;

public class DpiScaling
{
	private static float _cachedDeviceScale;

	[DllImport("shcore.dll")]
	public static extern void GetScaleFactorForMonitor(IntPtr hMon, out uint pScale);

	[DllImport("user32.dll")]
	public static extern IntPtr MonitorFromWindow(IntPtr hwnd, UInt32 dwFlags);

	public static float GetDeviceScale()
	{
		if (_cachedDeviceScale == 0)
		{
			IntPtr hMon = MonitorFromWindow(IntPtr.Zero, 0x1);
			GetScaleFactorForMonitor(hMon, out uint scale);
			// GetScaleFactorForMonitor returns an enum, however someone was nice enough to set the enum's values to match the scaling.
			_cachedDeviceScale = scale / 100f;
		}

		return _cachedDeviceScale;
	}

	public static Rect ScaleViewRect(Rect rect)
	{
		return new Rect(rect.X, rect.Y, (int)Math.Ceiling(rect.Width * (1 / GetDeviceScale())), (int)Math.Ceiling(rect.Height * (1 / GetDeviceScale())));
	}
}