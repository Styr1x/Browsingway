using Browsingway.Common.Ipc;
using System;
using System.Threading.Tasks;

namespace Browsingway.Common;

public class RendererRpc(string name) : IpcBase(name)
{
	// Declarative state sync
	public event Action<SyncOverlaysMessage>? SyncOverlays;

	// Imperative actions (user-triggered, not state)
	public event Action<NavigateMessage>? Navigate;
	public event Action<DebugMessage>? Debug;
	public event Action<MouseButtonMessage>? MouseButton;
	public event Action<KeyEventMessage>? KeyEvent;
	public event Action<GoBackMessage>? GoBack;
	public event Action<GoForwardMessage>? GoForward;
	public event Action<ReloadMessage>? Reload;

	// Calls to plugin
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

	public async Task UrlChanged(Guid id, string url, string? title = null, string? faviconUrl = null)
	{
		await SendCall(new RpcCall() { UrlChanged = new UrlChangedMessage() { Guid = id.ToByteArray(), Url = url, Title = title, FaviconUrl = faviconUrl } });
	}

	protected override void HandleCall(RpcCall call)
	{
		switch (call)
		{
			case { SyncOverlays: not null }:
				SyncOverlays?.Invoke(call.SyncOverlays);
				break;
			case { Navigate: not null }:
				Navigate?.Invoke(call.Navigate);
				break;
			case { Debug: not null }:
				Debug?.Invoke(call.Debug);
				break;
			case { MouseButton: not null }:
				MouseButton?.Invoke(call.MouseButton);
				break;
			case { KeyEvent: not null }:
				KeyEvent?.Invoke(call.KeyEvent);
				break;
			case { GoBack: not null }:
				GoBack?.Invoke(call.GoBack);
				break;
			case { GoForward: not null }:
				GoForward?.Invoke(call.GoForward);
				break;
			case { Reload: not null }:
				Reload?.Invoke(call.Reload);
				break;
		}
	}
}