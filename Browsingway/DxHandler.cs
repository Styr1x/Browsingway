using Dalamud.Plugin;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace Browsingway;

internal static class DxHandler
{
	private static unsafe ID3D11Device* _device;
	public static unsafe ID3D11Device* Device => _device;
	public static IntPtr WindowHandle { get; private set; }
	public static LUID AdapterLuid { get; private set; }

	public static unsafe void Initialise(IDalamudPluginInterface pluginInterface)
	{
		_device = (ID3D11Device*)pluginInterface.UiBuilder.DeviceHandle;

		// Grab the window handle, we'll use this for setting up our wndproc hook
		WindowHandle = pluginInterface.UiBuilder.WindowHandlePtr;

		// Get the game's device adapter, we'll need that as a reference for the render process.
		IDXGIDevice* dxgiDevice;
		Guid dxgiDeviceGuid = typeof(IDXGIDevice).GUID;
		HRESULT hr = _device->QueryInterface(&dxgiDeviceGuid, (void**)&dxgiDevice);
		if (hr.FAILED)
		{
			throw new Exception($"Failed to query IDXGIDevice: {hr}");
		}

		IDXGIAdapter* adapter;
		hr = dxgiDevice->GetAdapter(&adapter);
		if (hr.FAILED)
		{
			dxgiDevice->Release();
			throw new Exception($"Failed to get adapter: {hr}");
		}

		DXGI_ADAPTER_DESC desc;
		hr = adapter->GetDesc(&desc);
		if (hr.FAILED)
		{
			adapter->Release();
			dxgiDevice->Release();
			throw new Exception($"Failed to get adapter description: {hr}");
		}

		AdapterLuid = desc.AdapterLuid;

		adapter->Release();
		dxgiDevice->Release();
	}

	public static void Shutdown()
	{
		// Device is owned by Dalamud, don't release it
		unsafe { _device = null; }
	}
}