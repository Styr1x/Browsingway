using Browsingway.Common;
using CefSharp;
using CefSharp.Structs;
using SharedMemory;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Size = System.Drawing.Size;

namespace Browsingway.Renderer.RenderHandlers;

internal class BitmapBufferRenderHandler : BaseRenderHandler
{
	// CEF buffers are 32-bit BGRA
	private const byte _bytesPerPixel = 4;
	private readonly CircularBuffer _frameInfoBuffer;

	private BufferReadWrite? _bitmapBuffer;

	private ConcurrentBag<SharedBuffer> _obsoleteBuffers = new();
	private byte[]? _popupBuffer;
	private Rect _popupRect;

	private bool _popupVisible;
	private Size _size;

	public BitmapBufferRenderHandler(Size size)
	{
		_size = size;

		BuildBitmapBuffer(size);

		_frameInfoBuffer = new CircularBuffer(
			$"BrowsingwayFrameInfoBuffer{Guid.NewGuid()}",
			5,
			Marshal.SizeOf(typeof(BitmapFrame))
		);
	}

	public string? BitmapBufferName => _bitmapBuffer?.Name;
	public string FrameInfoBufferName => _frameInfoBuffer.Name;

	public override void Dispose()
	{
		_frameInfoBuffer.Dispose();
	}

	public override void Resize(Size newSize)
	{
		// If new size is same as current, noop
		if (
			newSize.Width == _size.Width &&
			newSize.Height == _size.Height
		)
		{
			return;
		}

		// Build new buffers & set up on instance
		_size = newSize;
		BuildBitmapBuffer(newSize);
	}

	protected override byte GetAlphaAt(int x, int y)
	{
		int rowPitch = _size.Width * _bytesPerPixel;

		int cursorAlphaOffset = 0
		                        + (Math.Min(Math.Max(x, 0), _size.Width - 1) * _bytesPerPixel)
		                        + (Math.Min(Math.Max(y, 0), _size.Height - 1) * rowPitch)
		                        + 3;

		byte alpha = 255;
		try
		{
			_bitmapBuffer?.Read(ptr =>
			{
				alpha = Marshal.ReadByte(ptr, cursorAlphaOffset);
			});
		}
		catch
		{
			Console.Error.WriteLine("Failed to read alpha value from bitmap buffer.");
			alpha = 255;
		}

		return alpha;
	}

	public override Rect GetViewRect()
	{
		return DpiScaling.ScaleViewRect(new Rect(0, 0, _size.Width, _size.Height));
	}

	public override void OnPaint(PaintElementType type, Rect dirtyRect, IntPtr buffer, int width, int height)
	{
		int length = _bytesPerPixel * width * height;

		// If this is a popup render, copy it across to the buffer.
		if (type != PaintElementType.View)
		{
			if (_popupBuffer is not null)
			{
				Marshal.Copy(buffer, _popupBuffer, 0, length);
			}

			return;
		}

		// If the paint size does not match our buffer size, we're likely resizing and paint hasn't caught up. Noop.
		if (width != _size.Width && height != _size.Height) { return; }

		BitmapFrame frame = new()
		{
			Length = length,
			Width = width,
			Height = height,
			DirtyX = dirtyRect.X,
			DirtyY = dirtyRect.Y,
			DirtyWidth = dirtyRect.Width,
			DirtyHeight = dirtyRect.Height
		};

		WriteToBuffers(frame, buffer, true);

		// Intersect with dirty?
		if (_popupVisible)
		{
			BitmapFrame popupFrame = new()
			{
				Length = length,
				Width = width,
				Height = height,
				DirtyX = _popupRect.X,
				DirtyY = _popupRect.Y,
				DirtyWidth = _popupRect.Width,
				DirtyHeight = _popupRect.Height
			};
			GCHandle handle = GCHandle.Alloc(_popupBuffer, GCHandleType.Pinned);
			WriteToBuffers(popupFrame, handle.AddrOfPinnedObject(), false);
			handle.Free();
		}

		// Render is complete, clean up obsolete buffers
		ConcurrentBag<SharedBuffer> obsoleteBuffers = _obsoleteBuffers;
		_obsoleteBuffers = new ConcurrentBag<SharedBuffer>();
		foreach (SharedBuffer toDispose in obsoleteBuffers) { toDispose.Dispose(); }
	}

	public override void OnPopupShow(bool show)
	{
		_popupVisible = show;
	}

	public override void OnPopupSize(Rect rect)
	{
		_popupRect = rect;
		_popupBuffer = new byte[rect.Width * rect.Height * _bytesPerPixel];
	}

	private void BuildBitmapBuffer(Size size)
	{
		BufferReadWrite? oldBitmapBuffer = _bitmapBuffer;

		string bitmapBufferName = $"BrowsingwayBitmapBuffer{Guid.NewGuid()}";
		_bitmapBuffer = new BufferReadWrite(bitmapBufferName, size.Width * size.Height * _bytesPerPixel);

		// Mark the old buffer for disposal
		if (oldBitmapBuffer != null) { _obsoleteBuffers.Add(oldBitmapBuffer); }
	}

	private void WriteToBuffers(BitmapFrame frame, IntPtr buffer, bool offsetFromSource)
	{
		// Not using read/write locks because I'm a cowboy (and there seems to be a race cond in the locking mechanism)
		try
		{
			WriteDirtyRect(frame, buffer, offsetFromSource);
			_frameInfoBuffer.Write(ref frame);
		}
		catch (AccessViolationException e)
		{
			Console.WriteLine($"Error writing to buffer, nooping frame on {_bitmapBuffer?.Name}: {e.Message}");
		}
	}

	private void WriteDirtyRect(BitmapFrame frame, IntPtr buffer, bool offsetFromSource)
	{
		if (_bitmapBuffer is null)
		{
			return;
		}

		// Write each row as a dirty stripe
		for (int row = frame.DirtyY; row < frame.DirtyY + frame.DirtyHeight; row++)
		{
			int position = (row * frame.Width * _bytesPerPixel) + (frame.DirtyX * _bytesPerPixel);
			int bufferOffset = offsetFromSource
				? position
				: (row - frame.DirtyY - 1) * frame.DirtyWidth * _bytesPerPixel;
			_bitmapBuffer.Write(
				buffer + bufferOffset,
				frame.DirtyWidth * _bytesPerPixel,
				position
			);
		}
	}
}