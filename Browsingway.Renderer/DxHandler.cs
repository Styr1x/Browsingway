using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.DirectX.D3D11_CREATE_DEVICE_FLAG;
using static TerraFX.Interop.DirectX.D3D_DRIVER_TYPE;

namespace Browsingway.Renderer;

internal static unsafe class DxHandler
{
	private static ID3D11Device* _device;

	public static ID3D11Device* Device => _device;

	public static bool Initialise(LUID adapterLuid)
	{
		// Find the adapter matching the luid from the parent process
		IDXGIFactory1* factory;
		Guid factoryGuid = typeof(IDXGIFactory1).GUID;
		HRESULT hr = CreateDXGIFactory1(&factoryGuid, (void**)&factory);
		if (hr.FAILED)
		{
			Console.Error.WriteLine($"FATAL: Could not create DXGI factory: {hr}");
			return false;
		}

		IDXGIAdapter* gameAdapter = null;
		List<LUID> foundLuids = new();

		uint i = 0;
		IDXGIAdapter* adapter;
		while (factory->EnumAdapters(i, &adapter) != DXGI.DXGI_ERROR_NOT_FOUND)
		{
			DXGI_ADAPTER_DESC desc;
			adapter->GetDesc(&desc);
			foundLuids.Add(desc.AdapterLuid);

			if (desc.AdapterLuid.HighPart == adapterLuid.HighPart && desc.AdapterLuid.LowPart == adapterLuid.LowPart)
			{
				gameAdapter = adapter;
				break;
			}

			adapter->Release();
			i++;
		}

		if (gameAdapter == null)
		{
			factory->Release();
			string foundLuidsStr = string.Join(",", foundLuids);
			Console.Error.WriteLine($"FATAL: Could not find adapter matching game adapter LUID {adapterLuid}. Found: {foundLuidsStr}.");
			return false;
		}

		// Use the adapter to build the device we'll use
		D3D11_CREATE_DEVICE_FLAG flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
#if DEBUG
		flags |= D3D11_CREATE_DEVICE_DEBUG;
#endif

		ID3D11Device* device;
		ID3D11DeviceContext* context;
		hr = D3D11CreateDevice(
			gameAdapter,
			D3D_DRIVER_TYPE_UNKNOWN, // Must be UNKNOWN when adapter is specified
			HMODULE.NULL,
			(uint)flags,
			null,
			0,
			D3D11.D3D11_SDK_VERSION,
			&device,
			null,
			&context);

		gameAdapter->Release();
		factory->Release();

		if (hr.FAILED)
		{
			Console.Error.WriteLine($"FATAL: Could not create D3D11 device: {hr}");
			return false;
		}

		// Release the immediate context - we get it as needed
		context->Release();

		_device = device;
		return true;
	}

	public static void Shutdown()
	{
		if (_device != null)
		{
			_device->Release();
			_device = null;
		}
	}
}