/*using System;

namespace Browsingway.Common;
// TODO: I should probably split this file up it's getting a bit silly


// DONE
public struct BitmapFrame
{
	public int Length;
	public int Width;
	public int Height;
	public int DirtyX;
	public int DirtyY;
	public int DirtyWidth;
	public int DirtyHeight;
}

// DONE
[Flags]
public enum FrameTransportMode
{
	None = 0,
	SharedTexture = 1 << 0,
	BitmapBuffer = 1 << 1
}

#region Downstream IPC

[Serializable]
public class DownstreamIpcRequest
{
}

// DONE
[Serializable]
public class NewInlayRequest : DownstreamIpcRequest
{
	public int Framerate;
	public FrameTransportMode FrameTransportMode;
	public Guid Guid;
	public required string Id;
	public int Height;
	public string Url = null!;
	public int Width;
	public float Zoom;
	public bool Muted;
	public string CustomCss = null!;
}

// DONE
[Serializable]
public class ResizeInlayRequest : DownstreamIpcRequest
{
	public Guid Guid;
	public int Height;
	public int Width;
}

// DONE?
[Serializable]
public class FrameTransportResponse
{
}

// DONE?
[Serializable]
public class TextureHandleResponse : FrameTransportResponse
{
	public IntPtr TextureHandle;
}

// DONE?
[Serializable]
public class BitmapBufferResponse : FrameTransportResponse
{
	public string BitmapBufferName = null!;
	public string FrameInfoBufferName = null!;
}

// DONE
[Serializable]
public class NavigateInlayRequest : DownstreamIpcRequest
{
	public Guid Guid;
	public string Url = null!;
}

// DONE
[Serializable]
public class InjectUserCssRequest : DownstreamIpcRequest
{
	public Guid Guid;
	public string Css = null!;
}

// DONE
[Serializable]
public class ZoomInlayRequest : DownstreamIpcRequest
{
	public Guid Guid;
	public float Zoom;
}

// DONE
[Serializable]
public class MuteInlayRequest : DownstreamIpcRequest
{
	public Guid Guid;
	public bool Mute;
}

// DONE
[Serializable]
public class DebugInlayRequest : DownstreamIpcRequest
{
	public Guid Guid;
}

// DONE
[Serializable]
public class RemoveInlayRequest : DownstreamIpcRequest
{
	public Guid Guid;
}

[Flags]
public enum InputModifier
{
	None = 0,
	Shift = 1 << 0,
	Control = 1 << 1,
	Alt = 1 << 2
}

[Flags]
public enum MouseButton
{
	None = 0,
	Primary = 1 << 0,
	Secondary = 1 << 1,
	Tertiary = 1 << 2,
	Fourth = 1 << 3,
	Fifth = 1 << 4
}

[Serializable]
public class MouseEventRequest : DownstreamIpcRequest
{
	public MouseButton Double;

	// The following button fields represent changes since the previous event, not current state
	// TODO: May be approaching being advantageous for button->fields map
	public MouseButton Down;
	public Guid Guid;
	public bool Leaving;
	public InputModifier Modifier;
	public MouseButton Up;
	public float WheelX;
	public float WheelY;
	public float X;
	public float Y;
}

public enum KeyEventType
{
	KeyDown,
	KeyUp,
	Character
}

[Serializable]
public class KeyEventRequest : DownstreamIpcRequest
{
	public Guid Guid;
	public int Msg;
	public int WParam;
	public int LParam;
}

#endregion

#region Upstream IPC

[Serializable]
public class UpstreamIpcRequest
{
}

[Serializable]
public class ReadyNotificationRequest : UpstreamIpcRequest
{
	public FrameTransportMode AvailableTransports;
}

// Akk, did you really write out every supported value of the cursor property despite both sides of the IPC not supporting the full set?
// Yes. Yes I did.
public enum Cursor
{
	Default,
	None,
	ContextMenu,
	Help,
	Pointer,
	Progress,
	Wait,
	Cell,
	Crosshair,
	Text,
	VerticalText,
	Alias,
	Copy,
	Move,
	NoDrop,
	NotAllowed,
	Grab,
	Grabbing,
	AllScroll,
	ColResize,
	RowResize,
	NResize,
	EResize,
	SResize,
	WResize,
	NeResize,
	NwResize,
	SeResize,
	SwResize,
	EwResize,
	NsResize,
	NeswResize,
	NwseResize,
	ZoomIn,
	ZoomOut,

	// Special case value - cursor is on a fully-transparent section of the page, and should not capture
	BrowsingwayNoCapture
}

[Serializable]
public class SetCursorRequest : UpstreamIpcRequest
{
	public Cursor Cursor;
	public Guid Guid;
}

#endregion*/