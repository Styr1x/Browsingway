using Browsingway.Common.Ipc;
using System;
using System.Threading.Tasks;

namespace Browsingway.Common;

public class BrowsingwayRpc(string name) : IpcBase(name)
{
	// calls from the renderer
	public event Action<SetCursorMessage>? SetCursor;
	public event Action<RendererReadyMessage>? RendererReady;
	public event Action<UpdateTextureMessage>? UpdateTexture;

	// calls to the renderer
	public async Task NewOverlay(NewOverlayMessage msg)
	{
		await SendCall(new RpcCall() { NewOverlay = msg });
	}

	public async Task Navigate(Guid id, string url)
	{
		await SendCall(new RpcCall() { Navigate = new NavigateMessage() { Guid = id.ToByteArray(), Url = url } });
	}

	public async Task ResizeOverlay(Guid id, int width, int height)
	{
		await SendCall(new RpcCall() { ResizeOverlay = new ResizeOverlayMessage() { Guid = id.ToByteArray(), Width = width, Height = height } });
	}

	public async Task InjectUserCss(Guid id, string css)
	{
		await SendCall(new RpcCall() { InjectUserCss = new InjectUserCssMessage() { Guid = id.ToByteArray(), Css = css } });
	}

	public async Task Zoom(Guid id, float zoom)
	{
		await SendCall(new RpcCall() { Zoom = new ZoomMessage() { Guid = id.ToByteArray(), Zoom = zoom } });
	}

	public async Task Mute(Guid id, bool mute)
	{
		await SendCall(new RpcCall() { Mute = new MuteMessage() { Guid = id.ToByteArray(), Mute = mute } });
	}

	public async Task Debug(Guid id)
	{
		await SendCall(new RpcCall() { Debug = new DebugMessage() { Guid = id.ToByteArray() } });
	}

	public async Task RemoveOverlay(Guid id)
	{
		await SendCall(new RpcCall() { RemoveOverlay = new RemoveOverlayMessage() { Guid = id.ToByteArray() } });
	}

	public async Task MouseButton(MouseButtonMessage msg)
	{
		await SendCall(new RpcCall() { MouseButton = msg });
	}

	public async Task KeyEvent(Guid id, int msg, int wParam, int lParam)
	{
		await SendCall(new RpcCall() { KeyEvent = new KeyEventMessage() { Guid = id.ToByteArray(), Msg = msg, WParam = wParam, LParam = lParam } });
	}

	protected override void HandleCall(RpcCall call)
	{
		switch (call)
		{
			case { SetCursor: not null }:
				SetCursor?.Invoke(call.SetCursor);
				break;
			case { RendererReady: not null }:
				RendererReady?.Invoke(call.RendererReady);
				break;
			case { UpdateTexture: not null }:
				UpdateTexture?.Invoke(call.UpdateTexture);
				break;
		}
	}
}