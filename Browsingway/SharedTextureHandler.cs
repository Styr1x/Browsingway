using Dalamud.Bindings.ImGui;
using System.Numerics;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace Browsingway;

internal unsafe class SharedTextureHandler : IDisposable
{
	private readonly ID3D11ShaderResourceView* _view;
	private readonly ID3D11Texture2D* _texture;
	private readonly Vector2 _size;
	private readonly ImTextureID _textureId;

	public SharedTextureHandler(IntPtr handle)
	{
		ID3D11Device* device = DxHandler.Device;
		if (device == null)
		{
			throw new Exception("Device is null");
		}

		// Open the shared resource
		Guid texture2DGuid = typeof(ID3D11Texture2D).GUID;
		void* texturePtr;
		HRESULT hr = device->OpenSharedResource((HANDLE)handle, &texture2DGuid, &texturePtr);
		if (hr.FAILED)
		{
			throw new Exception($"Could not open shared resource: {hr}");
		}

		_texture = (ID3D11Texture2D*)texturePtr;

		// Get the texture description
		D3D11_TEXTURE2D_DESC texDesc;
		_texture->GetDesc(&texDesc);

		// Create the shader resource view
		D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = new()
		{
			Format = texDesc.Format, ViewDimension = D3D_SRV_DIMENSION.D3D_SRV_DIMENSION_TEXTURE2D, Texture2D = new D3D11_TEX2D_SRV {MostDetailedMip = 0, MipLevels = texDesc.MipLevels}
		};

		ID3D11ShaderResourceView* view;
		hr = device->CreateShaderResourceView((ID3D11Resource*)_texture, &srvDesc, &view);
		if (hr.FAILED)
		{
			_texture->Release();
			throw new Exception($"Could not create shader resource view: {hr}");
		}

		_view = view;
		_size = new Vector2(texDesc.Width, texDesc.Height);
		_textureId = new ImTextureID((nint)_view);
	}

	public void Dispose()
	{
		_view->Release();
		_texture->Release();
	}

	public void Render()
	{
		ImGui.Image(_textureId, _size);
	}
}