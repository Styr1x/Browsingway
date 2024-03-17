using Browsingway.Common.Ipc;
using System;
using System.Threading.Tasks;

namespace Browsingway.Common;

public class RendererRpc(string name) : IpcBase(name)
{
	public event Action<NewOverlayMessage>? NewOverlay;
	public event Action<NavigateMessage>? Navigate;
	public event Action<ResizeOverlayMessage>? ResizeOverlay;
	public event Action<InjectUserCssMessage>? InjectUserCss;
	public event Action<ZoomMessage>? Zoom;
	public event Action<MuteMessage>? Mute;
	public event Action<DebugMessage>? Debug;
	public event Action<RemoveOverlayMessage>? RemoveOverlay;
	public event Action<MouseButtonMessage>? MouseButton;
	public event Action<KeyEventMessage>? KeyEvent;

	public async Task RendererReady(bool bHasDxSharedTexturesSupport)
	{
		await SendCall(new RpcCall() { RendererReady = new RendererReadyMessage() { HasDxSharedTexturesSupport = bHasDxSharedTexturesSupport } });
	}

	public async Task UpdateTexture(Guid id, IntPtr textureHandle)
	{
		await SendCall(new RpcCall() { UpdateTexture = new UpdateTextureMessage() { Guid = id.ToByteArray(), TextureHandle = (ulong)textureHandle } });
	}

	public async Task SetCursor(SetCursorMessage msg)
	{
		await SendCall(new RpcCall() { SetCursor = msg });
	}

	protected override void HandleCall(RpcCall call)
	{
		switch (call)
		{
			case { NewOverlay: not null }:
				NewOverlay?.Invoke(call.NewOverlay);
				break;
			case { Navigate: not null }:
				Navigate?.Invoke(call.Navigate);
				break;
			case { ResizeOverlay: not null }:
				ResizeOverlay?.Invoke(call.ResizeOverlay);
				break;
			case { InjectUserCss: not null }:
				InjectUserCss?.Invoke(call.InjectUserCss);
				break;
			case { Zoom: not null }:
				Zoom?.Invoke(call.Zoom);
				break;
			case { Mute: not null }:
				Mute?.Invoke(call.Mute);
				break;
			case { Debug: not null }:
				Debug?.Invoke(call.Debug);
				break;
			case { RemoveOverlay: not null }:
				RemoveOverlay?.Invoke(call.RemoveOverlay);
				break;
			case { MouseButton: not null }:
				MouseButton?.Invoke(call.MouseButton);
				break;
			case { KeyEvent: not null }:
				KeyEvent?.Invoke(call.KeyEvent);
				break;
		}
	}
}