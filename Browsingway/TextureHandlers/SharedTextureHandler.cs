using Browsingway.Common;
using ImGuiNET;
using ImGuiScene;
using System.Numerics;
using D3D = SharpDX.Direct3D;
using D3D11 = SharpDX.Direct3D11;

namespace Browsingway.TextureHandlers;

internal class SharedTextureHandler : ITextureHandler
{
	private readonly TextureWrap textureWrap;

	public SharedTextureHandler(TextureHandleResponse response)
	{
		D3D11.Texture2D? textureSource = DxHandler.Device?.OpenSharedResource<D3D11.Texture2D>(response.TextureHandle);
		if (textureSource is null)
		{
			throw new Exception("Could not initialize shared texture");
		}

		D3D11.ShaderResourceView view = new(DxHandler.Device, textureSource, new D3D11.ShaderResourceViewDescription { Format = textureSource.Description.Format, Dimension = D3D.ShaderResourceViewDimension.Texture2D, Texture2D = { MipLevels = textureSource.Description.MipLevels } });
		textureWrap = new D3DTextureWrap(view, textureSource.Description.Width, textureSource.Description.Height);
	}

	public void Dispose()
	{
		textureWrap.Dispose();
	}

	public void Render()
	{
		ImGui.Image(textureWrap.ImGuiHandle, new Vector2(textureWrap.Width, textureWrap.Height));
	}
}