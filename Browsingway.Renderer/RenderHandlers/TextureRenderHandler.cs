using CefSharp;
using CefSharp.Structs;
using SharpDX.DXGI;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using D3D11 = SharpDX.Direct3D11;
using Size = System.Drawing.Size;

namespace Browsingway.Renderer.RenderHandlers;

internal class TextureRenderHandler : BaseRenderHandler
{
	// CEF buffers are 32-bit BGRA
	private const byte _bytesPerPixel = 4;
	private int _bufferHeight;

	// Transparent background click-through state
	private IntPtr _bufferPtr;
	private int _bufferWidth;
	private ConcurrentBag<D3D11.Texture2D> _obsoleteTextures = new();

	private Rect _popupRect;
	private D3D11.Texture2D? _popupTexture;
	private bool _popupVisible;
	private D3D11.Texture2D _sharedTexture;

	private IntPtr _sharedTextureHandle = IntPtr.Zero;

	public TextureRenderHandler(Size size)
	{
		_sharedTexture = BuildViewTexture(size);
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
		_popupTexture?.Dispose();

		foreach (D3D11.Texture2D texture in _obsoleteTextures) { texture.Dispose(); }
	}

	public override void Resize(Size size)
	{
		D3D11.Texture2D oldTexture = _sharedTexture;
		_sharedTexture = BuildViewTexture(size);
		_obsoleteTextures.Add(oldTexture);

		// Need to clear the cached handle value
		// TODO: Maybe I should just avoid the lazy cache and do it eagerly on _sharedTexture build.
		_sharedTextureHandle = IntPtr.Zero;
	}

	// Nasty shit needs nasty attributes.
	[HandleProcessCorruptedStateExceptions]
	protected override byte GetAlphaAt(int x, int y)
	{
		int rowPitch = _bufferWidth * _bytesPerPixel;

		// Get the offset for the alpha of the cursor's current position. Bitmap buffer is BGRA, so +3 to get alpha byte
		int cursorAlphaOffset = 0
		                        + (Math.Min(Math.Max(x, 0), _bufferWidth - 1) * _bytesPerPixel)
		                        + (Math.Min(Math.Max(y, 0), _bufferHeight - 1) * rowPitch)
		                        + 3;

		byte alpha;
		try { alpha = Marshal.ReadByte(_bufferPtr + cursorAlphaOffset); }
		catch
		{
			Console.Error.WriteLine("Failed to read alpha value from cef buffer.");
			return 255;
		}

		return alpha;
	}

	private D3D11.Texture2D BuildViewTexture(Size size)
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
			OptionFlags = D3D11.ResourceOptionFlags.Shared
		});
	}

	public override Rect GetViewRect()
	{
		// There's a very small chance that OnPaint's cleanup will delete the current _sharedTexture midway through this function -
		// Try a few times just in case before failing out with an obviously-wrong value
		// hi adam
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
		D3D11.Texture2D targetTexture = type switch
		{
			PaintElementType.View => _sharedTexture,
			PaintElementType.Popup => _popupTexture,
			_ => throw new Exception($"Unknown paint type {type}")
		} ?? throw new Exception($"Target texture is null for paint type {type}");

		// Nasty hack; we're keeping a ref to the view buffer for pixel lookups without going through DX
		if (type == PaintElementType.View)
		{
			_bufferPtr = buffer;
			_bufferWidth = width;
			_bufferHeight = height;
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

		// Only need to do composition + flush on primary _sharedTexture
		if (type != PaintElementType.View) { return; }

		// Intersect with dirty?
		if (_popupVisible)
		{
			context.CopySubresourceRegion(_popupTexture, 0, null, targetTexture, 0, _popupRect.X, _popupRect.Y);
		}

		context.Flush();

		// Rendering is complete, clean up any obsolete textures
		ConcurrentBag<D3D11.Texture2D> textures = _obsoleteTextures;
		_obsoleteTextures = new ConcurrentBag<D3D11.Texture2D>();
		foreach (D3D11.Texture2D tex in textures) { tex.Dispose(); }
	}

	public override void OnPopupShow(bool show)
	{
		_popupVisible = show;
	}

	public override void OnPopupSize(Rect rect)
	{
		_popupRect = rect;

		// I'm really not sure if this happens. If it does, frequently - will probably need 2x shared textures and some jazz.
		D3D11.Texture2DDescription texDesc = _sharedTexture.Description;
		if (rect.Width > texDesc.Width || rect.Height > texDesc.Height)
		{
			Console.Error.WriteLine($"Trying to build popup layer ({rect.Width}x{rect.Height}) larger than primary surface ({texDesc.Width}x{texDesc.Height}).");
		}

		// Get a reference to the old _sharedTexture, we'll make sure to assign a new _sharedTexture before disposing the old one.
		D3D11.Texture2D? oldTexture = _popupTexture;

		// Build a _sharedTexture for the new sized popup
		_popupTexture = new D3D11.Texture2D(_sharedTexture.Device, new D3D11.Texture2DDescription
		{
			Width = rect.Width,
			Height = rect.Height,
			MipLevels = 1,
			ArraySize = 1,
			Format = Format.B8G8R8A8_UNorm,
			SampleDescription = new SampleDescription(1, 0),
			Usage = D3D11.ResourceUsage.Default,
			BindFlags = D3D11.BindFlags.ShaderResource,
			CpuAccessFlags = D3D11.CpuAccessFlags.None,
			OptionFlags = D3D11.ResourceOptionFlags.None
		});

		oldTexture?.Dispose();
	}
}