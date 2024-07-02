using Browsingway.Common.Ipc;
using CefSharp;
using CefSharp.Enums;
using CefSharp.OffScreen;
using CefSharp.Structs;
using SharpDX.DXGI;
using System.Collections.Concurrent;
using D3D11 = SharpDX.Direct3D11;
using Range = CefSharp.Structs.Range;
using Size = System.Drawing.Size;

namespace Browsingway.Renderer;

internal class TextureRenderHandler : IRenderHandler
{
	// CEF buffers are 32-bit BGRA
	private const byte _bytesPerPixel = 4;

	// TODO: replace with lockless implementation
	private readonly object _renderLock = new();

	// TODO: remove me
	private byte[] _alphaLookupBuffer = Array.Empty<byte>();
	private int _alphaLookupBufferHeight;
	private int _alphaLookupBufferWidth;

	private Cursor _cursor;

	// Transparent background click-through state
	private bool _cursorOnBackground;

	private ConcurrentBag<D3D11.Texture2D> _obsoleteTextures = new();

	private Rect _popupRect;
	private D3D11.Texture2D? _popupTexture;
	private bool _popupVisible;
	private D3D11.Texture2D _sharedTexture;

	private IntPtr _sharedTextureHandle = IntPtr.Zero;
	private D3D11.Texture2D _viewTexture;

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

	public event EventHandler<Cursor>? CursorChanged;

	public void Dispose()
	{
		_sharedTexture.Dispose();
		_viewTexture.Dispose();
		_popupTexture?.Dispose();

		foreach (D3D11.Texture2D texture in _obsoleteTextures) { texture.Dispose(); }
	}

	public Rect GetViewRect()
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

	public void OnAcceleratedPaint(PaintElementType type, Rect dirtyRect, AcceleratedPaintInfo acceleratedPaintInfo)
	{
		// TODO: use this instead of manual texture copying
		throw new NotImplementedException();
	}

	public void OnPaint(PaintElementType type, Rect dirtyRect, IntPtr buffer, int width, int height)
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
				int requiredBufferSize = width * height * _bytesPerPixel;
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
				Point popupPos = DpiScaling.ScaleScreenPoint(_popupRect.X, _popupRect.Y);
				context.CopySubresourceRegion(_popupTexture, 0, null, _sharedTexture, 0, popupPos.X, popupPos.Y);
			}

			context.Flush();

			// Rendering is complete, clean up any obsolete textures
			ConcurrentBag<D3D11.Texture2D> textures = _obsoleteTextures;
			_obsoleteTextures = new ConcurrentBag<D3D11.Texture2D>();
			foreach (D3D11.Texture2D tex in textures) { tex.Dispose(); }
		}
	}

	public void OnPopupShow(bool show)
	{
		_popupVisible = show;
	}

	public void OnPopupSize(Rect rect)
	{
		_popupRect = DpiScaling.ScaleScreenRect(rect);

		// I'm really not sure if this happens. If it does, frequently - will probably need 2x shared textures and some jazz.
		D3D11.Texture2DDescription texDesc = _sharedTexture.Description;
		if (_popupRect.Width > texDesc.Width || _popupRect.Height > texDesc.Height)
		{
			Console.Error.WriteLine(
				$"Trying to build popup layer ({_popupRect.Width}x{_popupRect.Height}) larger than primary surface ({texDesc.Width}x{texDesc.Height}).");
		}

		// Get a reference to the old _sharedTexture, we'll make sure to assign a new _sharedTexture before disposing the old one.
		D3D11.Texture2D? oldTexture = _popupTexture;

		// Build a _sharedTexture for the new sized popup
		_popupTexture = BuildViewTexture(new Size(_popupRect.Width, _popupRect.Height), false);

		oldTexture?.Dispose();
	}

	public ScreenInfo? GetScreenInfo()
	{
		return new ScreenInfo {DeviceScaleFactor = DpiScaling.GetDeviceScale()};
	}

	public bool GetScreenPoint(int viewX, int viewY, out int screenX, out int screenY)
	{
		screenX = viewX;
		screenY = viewY;

		return false;
	}

	public void OnVirtualKeyboardRequested(IBrowser browser, TextInputMode inputMode)
	{
	}

	public void OnImeCompositionRangeChanged(Range selectedRange, Rect[] characterBounds)
	{
	}

	public void OnCursorChange(IntPtr cursorPtr, CursorType type, CursorInfo customCursorInfo)
	{
		_cursor = EncodeCursor(type);

		// If we're on background, don't flag a cursor change
		if (!_cursorOnBackground) { CursorChanged?.Invoke(this, _cursor); }
	}

	public bool StartDragging(IDragData dragData, DragOperationsMask mask, int x, int y)
	{
		// Returning false to abort drag operations.
		return false;
	}

	public void UpdateDragCursor(DragOperationsMask operation)
	{
	}

	public void Resize(Size size)
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

	protected byte GetAlphaAt(int x, int y)
	{
		lock (_renderLock)
		{
			int rowPitch = _alphaLookupBufferWidth * _bytesPerPixel;

			// Get the offset for the alpha of the cursor's current position. Bitmap buffer is BGRA, so +3 to get alpha byte
			int cursorAlphaOffset = 0
			                        + (Math.Min(Math.Max(x, 0), _alphaLookupBufferWidth - 1) * _bytesPerPixel)
			                        + (Math.Min(Math.Max(y, 0), _alphaLookupBufferHeight - 1) * rowPitch)
			                        + 3;
			cursorAlphaOffset = cursorAlphaOffset < 0 ? 0 : cursorAlphaOffset;

			if (cursorAlphaOffset < _alphaLookupBuffer.Length)
			{
				return _alphaLookupBuffer[cursorAlphaOffset];
			}

			Console.WriteLine("Could not determine alpha value");
			return 255;
		}
	}

	private D3D11.Texture2D BuildViewTexture(Size size, bool isShared)
	{
		// Build _sharedTexture. Most of these properties are defined to match how CEF exposes the render buffer.
		return new D3D11.Texture2D(DxHandler.Device,
			new D3D11.Texture2DDescription
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

	private Rect GetViewRectInternal()
	{
		D3D11.Texture2DDescription texDesc = _sharedTexture.Description;
		return DpiScaling.ScaleViewRect(new Rect(0, 0, texDesc.Width, texDesc.Height));
	}

	public void SetMousePosition(int x, int y)
	{
		byte alpha = GetAlphaAt(x, y);

		// We treat 0 alpha as click through - if changed, fire off the event
		bool currentlyOnBackground = alpha == 0;
		if (currentlyOnBackground != _cursorOnBackground)
		{
			_cursorOnBackground = currentlyOnBackground;

			// EDGE CASE: if cursor transitions onto alpha:0 _and_ between two native cursor types, I guess this will be a race cond.
			// Not sure if should have two separate upstreams for them, or try and prevent the race. consider.
			CursorChanged?.Invoke(this, currentlyOnBackground ? Cursor.BrowsingwayNoCapture : _cursor);
		}
	}

	private Cursor EncodeCursor(CursorType cursor)
	{
		switch (cursor)
		{
			// CEF calls default "pointer", and pointer "hand".
			case CursorType.Pointer: return Cursor.Default;
			case CursorType.Cross: return Cursor.Crosshair;
			case CursorType.Hand: return Cursor.Pointer;
			case CursorType.IBeam: return Cursor.Text;
			case CursorType.Wait: return Cursor.Wait;
			case CursorType.Help: return Cursor.Help;
			case CursorType.EastResize: return Cursor.EResize;
			case CursorType.NorthResize: return Cursor.NResize;
			case CursorType.NortheastResize: return Cursor.NeResize;
			case CursorType.NorthwestResize: return Cursor.NwResize;
			case CursorType.SouthResize: return Cursor.SResize;
			case CursorType.SoutheastResize: return Cursor.SeResize;
			case CursorType.SouthwestResize: return Cursor.SwResize;
			case CursorType.WestResize: return Cursor.WResize;
			case CursorType.NorthSouthResize: return Cursor.NsResize;
			case CursorType.EastWestResize: return Cursor.EwResize;
			case CursorType.NortheastSouthwestResize: return Cursor.NeswResize;
			case CursorType.NorthwestSoutheastResize: return Cursor.NwseResize;
			case CursorType.ColumnResize: return Cursor.ColResize;
			case CursorType.RowResize: return Cursor.RowResize;

			// There isn't really support for panning right now. Default to all-scroll.
			case CursorType.MiddlePanning:
			case CursorType.EastPanning:
			case CursorType.NorthPanning:
			case CursorType.NortheastPanning:
			case CursorType.NorthwestPanning:
			case CursorType.SouthPanning:
			case CursorType.SoutheastPanning:
			case CursorType.SouthwestPanning:
			case CursorType.WestPanning:
				return Cursor.AllScroll;

			case CursorType.Move: return Cursor.Move;
			case CursorType.VerticalText: return Cursor.VerticalText;
			case CursorType.Cell: return Cursor.Cell;
			case CursorType.ContextMenu: return Cursor.ContextMenu;
			case CursorType.Alias: return Cursor.Alias;
			case CursorType.Progress: return Cursor.Progress;
			case CursorType.NoDrop: return Cursor.NoDrop;
			case CursorType.Copy: return Cursor.Copy;
			case CursorType.None: return Cursor.None;
			case CursorType.NotAllowed: return Cursor.NotAllowed;
			case CursorType.ZoomIn: return Cursor.ZoomIn;
			case CursorType.ZoomOut: return Cursor.ZoomOut;
			case CursorType.Grab: return Cursor.Grab;
			case CursorType.Grabbing: return Cursor.Grabbing;

			// Not handling custom for now
			case CursorType.Custom: return Cursor.Default;
		}

		// Unmapped cursor, log and default
		Console.WriteLine($"Switching to unmapped cursor type {cursor}.");
		return Cursor.Default;
	}
}