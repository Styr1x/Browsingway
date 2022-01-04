using Browsingway.Common;
using ImGuiNET;
using ImGuiScene;
using SharedMemory;
using SharpDX.DXGI;
using System.Collections.Concurrent;
using System.Numerics;
using D3D = SharpDX.Direct3D;
using D3D11 = SharpDX.Direct3D11;

namespace Browsingway.TextureHandlers;

internal class BitmapBufferTextureHandler : ITextureHandler
{
	private readonly BufferReadWrite _bitmapBuffer;
	private readonly CancellationTokenSource _cancellationTokenSource;
	private ConcurrentQueue<BitmapFrame> _frameQueue = new();
	private D3D11.Texture2D? _texture;
	private TextureWrap? _textureWrap;

	public BitmapBufferTextureHandler(BitmapBufferResponse response)
	{
		_cancellationTokenSource = new CancellationTokenSource();
		Thread frameBufferThread = new(FrameBufferThread);
		frameBufferThread.Start(new ThreadArguments { BufferName = response.FrameInfoBufferName, CancellationToken = _cancellationTokenSource.Token });

		_bitmapBuffer = new BufferReadWrite(response.BitmapBufferName);
	}

	public void Dispose()
	{
		_cancellationTokenSource.Cancel();
		_cancellationTokenSource.Dispose();
		if (!(_texture?.IsDisposed ?? true)) { _texture.Dispose(); }

		_textureWrap?.Dispose();
		_bitmapBuffer.Dispose();
	}

	public void Render()
	{
		// Render incoming frame info on the queue. Doing a queue swap to prevent edge cases where a slow game
		// paired with a fast renderer will loop dequeue infinitely.
		ConcurrentQueue<BitmapFrame> currentFrameQueue = _frameQueue;
		_frameQueue = new ConcurrentQueue<BitmapFrame>();
		while (currentFrameQueue.TryDequeue(out BitmapFrame frame))
		{
			RenderFrame(frame);
		}

		if (_textureWrap == null) { return; }

		ImGui.Image(_textureWrap.ImGuiHandle, new Vector2(_textureWrap.Width, _textureWrap.Height));
	}

	private void RenderFrame(BitmapFrame frame)
	{
		// Make sure there's a texture to render to
		// TODO: Can probably afford to remove width/height from frame, and just add the buffer name. this client code can then check that the buffer name matches what it expects, and noop if it doesn't
		if (_texture == null)
		{
			BuildTexture(frame.Width, frame.Height);
		}

		// If the details don't match our expected sizes, noop the frame to avoid a CTD.
		// This may "stick" and cause no rendering at all, but can be fixed by jiggling the size a bit, or reloading.
		if (
			_texture?.Description.Width != frame.Width
			|| _texture?.Description.Height != frame.Height
			|| _bitmapBuffer.BufferSize != frame.Length
		)
		{
			return;
		}

		// Calculate multipliers for the frame
		int depthPitch = frame.Length;
		int rowPitch = frame.Length / frame.Height;
		int bytesPerPixel = rowPitch / frame.Width;

		// Build the destination region for the dirty rect we're drawing
		D3D11.Texture2DDescription texDesc = _texture.Description;
		int sourceRegionOffset = (frame.DirtyX * bytesPerPixel) + (frame.DirtyY * rowPitch);
		D3D11.ResourceRegion destinationRegion = new()
		{
			Top = Math.Min(frame.DirtyY, texDesc.Height),
			Bottom = Math.Min(frame.DirtyY + frame.DirtyHeight, texDesc.Height),
			Left = Math.Min(frame.DirtyX, texDesc.Width),
			Right = Math.Min(frame.DirtyX + frame.DirtyWidth, texDesc.Width),
			Front = 0,
			Back = 1
		};

		// Write data from the buffer
		D3D11.DeviceContext? context = DxHandler.Device?.ImmediateContext;
		_bitmapBuffer.Read(ptr =>
		{
			context?.UpdateSubresource(_texture, 0, destinationRegion, ptr + sourceRegionOffset, rowPitch, depthPitch);
		});
	}

	private void BuildTexture(int width, int height)
	{
		// TODO: This should probably be a dynamic texture, with updates performed via mapping. Work it out.
		_texture = new D3D11.Texture2D(DxHandler.Device, new D3D11.Texture2DDescription
		{
			Width = width,
			Height = height,
			MipLevels = 1,
			ArraySize = 1,
			Format = Format.B8G8R8A8_UNorm,
			SampleDescription = new SampleDescription(1, 0),
			Usage = D3D11.ResourceUsage.Default,
			BindFlags = D3D11.BindFlags.ShaderResource,
			CpuAccessFlags = D3D11.CpuAccessFlags.None,
			OptionFlags = D3D11.ResourceOptionFlags.None
		});

		D3D11.ShaderResourceView view = new(DxHandler.Device, _texture, new D3D11.ShaderResourceViewDescription { Format = _texture.Description.Format, Dimension = D3D.ShaderResourceViewDimension.Texture2D, Texture2D = { MipLevels = _texture.Description.MipLevels } });

		_textureWrap = new D3DTextureWrap(view, _texture.Description.Width, _texture.Description.Height);
	}

	private void FrameBufferThread(object? arguments)
	{
		ThreadArguments args = (ThreadArguments)arguments!;
		// Open up a reference to the frame info buffer
		using CircularBuffer frameInfoBuffer = new(args.BufferName);

		// We're just looping the blocking read operation forever. Parent will abort the to shut down.
		while (true)
		{
			args.CancellationToken.ThrowIfCancellationRequested();

			frameInfoBuffer.Read(out BitmapFrame frame, Timeout.Infinite);
			_frameQueue.Enqueue(frame);
		}
		// ReSharper disable once FunctionNeverReturns
	}

	private struct ThreadArguments
	{
		public string BufferName;
		public CancellationToken CancellationToken;
	}
}