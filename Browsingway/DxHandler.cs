using Dalamud.Plugin;
using SharpDX.DXGI;
using D3D11 = SharpDX.Direct3D11;

namespace Browsingway;

internal static class DxHandler
{
	public static D3D11.Device? Device { get; private set; }
	public static IntPtr WindowHandle { get; private set; }
	public static long AdapterLuid { get; private set; }

	public static void Initialise(IDalamudPluginInterface pluginInterface)
	{
		Device = pluginInterface.UiBuilder.Device;
		//Device = new D3D11.Device(SharpDX.Direct3D.DriverType.Hardware, D3D11.DeviceCreationFlags.BgraSupport | D3D11.DeviceCreationFlags.Debug);

		// Grab the window handle, we'll use this for setting up our wndproc hook
		WindowHandle = pluginInterface.UiBuilder.WindowHandlePtr;

		// Get the game's device adapter, we'll need that as a reference for the render process.
		Device? dxgiDevice = Device.QueryInterface<Device>();
		AdapterLuid = dxgiDevice.Adapter.Description.Luid;
	}

	public static void Shutdown()
	{
		Device = null;
	}
}