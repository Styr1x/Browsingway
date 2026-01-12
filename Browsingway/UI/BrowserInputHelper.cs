using Browsingway.Common.Ipc;
using Dalamud.Bindings.ImGui;

namespace Browsingway.UI;

/// <summary>
/// Shared utilities for browser input handling between OverlayWindow and BrowserWindow.
/// </summary>
internal static class BrowserInputHelper
{
	/// <summary>
	/// Encodes ImGui mouse button states into the MouseButton flags used by the renderer.
	/// </summary>
	public static MouseButton EncodeMouseButtons(Span<bool> buttons)
	{
		MouseButton result = MouseButton.None;
		if (buttons[0]) result |= MouseButton.Primary;
		if (buttons[1]) result |= MouseButton.Secondary;
		if (buttons[2]) result |= MouseButton.Tertiary;
		if (buttons[3]) result |= MouseButton.Fourth;
		if (buttons[4]) result |= MouseButton.Fifth;
		return result;
	}

	/// <summary>
	/// Decodes the renderer's Cursor enum into an ImGui cursor type.
	/// </summary>
	public static ImGuiMouseCursor DecodeCursor(Cursor cursor) => cursor switch
	{
		Cursor.Default => ImGuiMouseCursor.Arrow,
		Cursor.None => ImGuiMouseCursor.None,
		Cursor.Pointer => ImGuiMouseCursor.Hand,
		Cursor.Text or Cursor.VerticalText => ImGuiMouseCursor.TextInput,
		Cursor.NResize or Cursor.SResize or Cursor.NsResize => ImGuiMouseCursor.ResizeNs,
		Cursor.EResize or Cursor.WResize or Cursor.EwResize => ImGuiMouseCursor.ResizeEw,
		Cursor.NeResize or Cursor.SwResize or Cursor.NeswResize => ImGuiMouseCursor.ResizeNesw,
		Cursor.NwResize or Cursor.SeResize or Cursor.NwseResize => ImGuiMouseCursor.ResizeNwse,
		_ => ImGuiMouseCursor.Arrow
	};
}
