using Browsingway.Common.Ipc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Browsingway.Common;

public class BrowsingwayRpc(string name) : IpcBase(name)
{
	// Events from the renderer
	public event Action<SetCursorMessage>? SetCursor;
	public event Action<RendererReadyMessage>? RendererReady;
	public event Action<UpdateTextureMessage>? UpdateTexture;
	public event Action<UrlChangedMessage>? UrlChanged;

	// Declarative state sync - sends all overlay states to renderer
	public async Task SyncOverlays(IReadOnlyList<OverlayState> overlays)
	{
		await SendCall(new RpcCall() { SyncOverlays = new SyncOverlaysMessage() { Overlays = overlays.ToList() } });
	}

	// Imperative actions (user-triggered, not state)
	public async Task Navigate(Guid id, string url)
	{
		await SendCall(new RpcCall() { Navigate = new NavigateMessage() { Guid = id.ToByteArray(), Url = url } });
	}

	public async Task Debug(Guid id)
	{
		await SendCall(new RpcCall() { Debug = new DebugMessage() { Guid = id.ToByteArray() } });
	}

	public async Task MouseButton(MouseButtonMessage msg)
	{
		await SendCall(new RpcCall() { MouseButton = msg });
	}

	public async Task KeyEvent(Guid id, int msg, int wParam, int lParam)
	{
		await SendCall(new RpcCall() { KeyEvent = new KeyEventMessage() { Guid = id.ToByteArray(), Msg = msg, WParam = wParam, LParam = lParam } });
	}

	public async Task GoBack(Guid id)
	{
		await SendCall(new RpcCall() { GoBack = new GoBackMessage() { Guid = id.ToByteArray() } });
	}

	public async Task GoForward(Guid id)
	{
		await SendCall(new RpcCall() { GoForward = new GoForwardMessage() { Guid = id.ToByteArray() } });
	}

	public async Task Reload(Guid id, bool ignoreCache = false)
	{
		await SendCall(new RpcCall() { Reload = new ReloadMessage() { Guid = id.ToByteArray(), IgnoreCache = ignoreCache } });
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
			case { UrlChanged: not null }:
				UrlChanged?.Invoke(call.UrlChanged);
				break;
		}
	}
}