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
		}
	}
}