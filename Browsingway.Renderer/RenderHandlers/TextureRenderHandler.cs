using CefSharp;
using CefSharp.Structs;
using SharpDX.DXGI;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using D3D11 = SharpDX.Direct3D11;
using Size = System.Drawing.Size;

namespace Browsingway.Renderer.RenderHandlers;

internal class TextureRenderHandler : BaseRenderHandler
{
	// CEF buffers are 32-bit BGRA
	private const byte _bytesPerPixel = 4;

	private ConcurrentBag<D3D11.Texture2D> _obsoleteTextures = new();

	private Rect _popupRect;
	private D3D11.Texture2D? _popupTexture;
	private D3D11.Texture2D _viewTexture;
	private bool _popupVisible;
	private D3D11.Texture2D _sharedTexture;

	private IntPtr _sharedTextureHandle = IntPtr.Zero;

	// TODO: remove me
	private byte[] _alphaLookupBuffer = Array.Empty<byte>();
	private int _alphaLookupBufferWidth;
	private int _alphaLookupBufferHeight;

	// TODO: replace with lockless implementation
	private readonly object _renderLock = new();

	public TextureRenderHandler(Size size)
	{
		_sharedTexture = BuildViewTexture(size, true);
		_viewTexture = BuildViewTexture(size, false);
	}

	public IntPtr SharedTextureHandle
	{
		get
		{
			if (_sharedTextureHandle == IntPtr.Zero)
			{
				using Resource? resource = _sharedTexture.QueryInterface<Resource>();
				_sharedTextureHandle = resource.SharedHandle;
			}

			return _sharedTextureHandle;
		}
	}

	public override void Dispose()
	{
		_sharedTexture.Dispose();
		_viewTexture.Dispose();
		_popupTexture?.Dispose();

		foreach (D3D11.Texture2D texture in _obsoleteTextures) { texture.Dispose(); }
	}

	public override void Resize(Size size)
	{
		lock (_renderLock)
		{
			// TODO: make this thread unsafe crap thread safe crap
			D3D11.Texture2D oldTexture1 = _sharedTexture;
			D3D11.Texture2D oldTexture2 = _viewTexture;
			_sharedTexture = BuildViewTexture(size, true);
			_viewTexture = BuildViewTexture(size, false);
			_obsoleteTextures.Add(oldTexture1);
			_obsoleteTextures.Add(oldTexture2);

			// Need to clear the cached handle value
			// TODO: Maybe I should just avoid the lazy cache and do it eagerly on _sharedTexture build.
			_sharedTextureHandle = IntPtr.Zero;
		}
	}

	protected override byte GetAlphaAt(int x, int y)
	{
		lock (_renderLock)
		{
			int rowPitch = _alphaLookupBufferWidth * _bytesPerPixel;

			// Get the offset for the alpha of the cursor's current position. Bitmap buffer is BGRA, so +3 to get alpha byte
			int cursorAlphaOffset = 0
			                        + (Math.Min(Math.Max(x, 0), _alphaLookupBufferWidth - 1) * _bytesPerPixel)
			                        + (Math.Min(Math.Max(y, 0), _alphaLookupBufferHeight - 1) * rowPitch)
			                        + 3;

			if (cursorAlphaOffset < _alphaLookupBuffer.Length)
				return _alphaLookupBuffer[cursorAlphaOffset];
			Console.WriteLine("Could not determine alpha value");
			return 255;
		}
	}

	private D3D11.Texture2D BuildViewTexture(Size size, bool isShared)
	{
		// Build _sharedTexture. Most of these properties are defined to match how CEF exposes the render buffer.
		return new D3D11.Texture2D(DxHandler.Device, new D3D11.Texture2DDescription
		{
			Width = size.Width,
			Height = size.Height,
			MipLevels = 1,
			ArraySize = 1,
			Format = Format.B8G8R8A8_UNorm,
			SampleDescription = new SampleDescription(1, 0),
			Usage = D3D11.ResourceUsage.Default,
			BindFlags = D3D11.BindFlags.ShaderResource,
			CpuAccessFlags = D3D11.CpuAccessFlags.None,
			OptionFlags = isShared ? D3D11.ResourceOptionFlags.Shared : D3D11.ResourceOptionFlags.None
		});
	}

	public override Rect GetViewRect()
	{
		// There's a very small chance that OnPaint's cleanup will delete the current _sharedTexture midway through this function -
		// Try a few times just in case before failing out with an obviously-wrong value
		// hi adam
		// TODO: proper threading model instead of shitty hacks
		for (int i = 0; i < 5; i++)
		{
			try { return GetViewRectInternal(); }
			catch (NullReferenceException) { }
		}

		return new Rect(0, 0, 1, 1);
	}

	private Rect GetViewRectInternal()
	{
		D3D11.Texture2DDescription texDesc = _sharedTexture.Description;
		return DpiScaling.ScaleViewRect(new Rect(0, 0, texDesc.Width, texDesc.Height));
	}

	public override void OnPaint(PaintElementType type, Rect dirtyRect, IntPtr buffer, int width, int height)
	{
		lock (_renderLock)
		{
			D3D11.Texture2D targetTexture = type switch
			{
				PaintElementType.View => _viewTexture,
				PaintElementType.Popup => _popupTexture,
				_ => throw new Exception($"Unknown paint type {type}")
			} ?? throw new Exception($"Target texture is null for paint type {type}");

			// keep buffer to make alpha checks later on.
			// TODO: make this a back and front buffer to atomic swap them
			if (type == PaintElementType.View)
			{
				// check if lookup buffer is big enough
				var requiredBufferSize = width * height * _bytesPerPixel;
				_alphaLookupBufferWidth = width;
				_alphaLookupBufferHeight = height;
				if (_alphaLookupBuffer.Length < requiredBufferSize)
				{
					_alphaLookupBuffer = new byte[width * height * _bytesPerPixel];
				}

				unsafe
				{
					fixed (void* dstBuffer = _alphaLookupBuffer)
					{
						Buffer.MemoryCopy(buffer.ToPointer(), dstBuffer, _alphaLookupBuffer.Length, requiredBufferSize);
					}
				}
			}

			// Calculate offset multipliers for the current buffer
			int rowPitch = width * _bytesPerPixel;
			int depthPitch = rowPitch * height;

			// Build the destination region for the dirty rect that we'll draw to
			D3D11.Texture2DDescription texDesc = targetTexture.Description;
			IntPtr sourceRegionPtr = buffer + (dirtyRect.X * _bytesPerPixel) + (dirtyRect.Y * rowPitch);
			D3D11.ResourceRegion destinationRegion = new()
			{
				Top = Math.Min(dirtyRect.Y, texDesc.Height),
				Bottom = Math.Min(dirtyRect.Y + dirtyRect.Height, texDesc.Height),
				Left = Math.Min(dirtyRect.X, texDesc.Width),
				Right = Math.Min(dirtyRect.X + dirtyRect.Width, texDesc.Width),
				Front = 0,
				Back = 1
			};

			// Draw to the target
			D3D11.DeviceContext? context = targetTexture.Device.ImmediateContext;
			context.UpdateSubresource(targetTexture, 0, destinationRegion, sourceRegionPtr, rowPitch, depthPitch);

			// composite final picture
			// draw view layer, first
			context.CopySubresourceRegion(_viewTexture, 0, null, _sharedTexture, 0);

			// draw popup layer if required
			if (_popupVisible)
			{
				var popupPos = DpiScaling.ScaleScreenPoint(_popupRect.X, _popupRect.Y);
				context.CopySubresourceRegion(_popupTexture, 0, null, _sharedTexture, 0, popupPos.X, popupPos.Y);
			}

			context.Flush();

			// Rendering is complete, clean up any obsolete textures
			ConcurrentBag<D3D11.Texture2D> textures = _obsoleteTextures;
			_obsoleteTextures = new ConcurrentBag<D3D11.Texture2D>();
			foreach (D3D11.Texture2D tex in textures) { tex.Dispose(); }
		}
	}

	public override void OnPopupShow(bool show)
	{
		_popupVisible = show;
	}

	public override void OnPopupSize(Rect rect)
	{
		_popupRect = DpiScaling.ScaleScreenRect(rect);

		// I'm really not sure if this happens. If it does, frequently - will probably need 2x shared textures and some jazz.
		D3D11.Texture2DDescription texDesc = _sharedTexture.Description;
		if (_popupRect.Width > texDesc.Width || _popupRect.Height > texDesc.Height)
		{
			Console.Error.WriteLine($"Trying to build popup layer ({_popupRect.Width}x{_popupRect.Height}) larger than primary surface ({texDesc.Width}x{texDesc.Height}).");
		}

		// Get a reference to the old _sharedTexture, we'll make sure to assign a new _sharedTexture before disposing the old one.
		D3D11.Texture2D? oldTexture = _popupTexture;

		// Build a _sharedTexture for the new sized popup
		_popupTexture = BuildViewTexture(new Size(_popupRect.Width, _popupRect.Height), false);

		oldTexture?.Dispose();
	}
}