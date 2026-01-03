using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace Browsingway.UI.Windows.SettingsTabs;

/// <summary>
/// Renders a visual preview of overlay positioning on screen.
/// Shows anchor points as small boxes (green when selected) and
/// the resulting overlay bounds as a blue rectangle.
/// </summary>
internal static class PositionVisualizer
{
	// Anchor point grid indices (3x3 grid)
	private static readonly Dictionary<ScreenPositionMode, (int col, int row)> AnchorPositions = new()
	{
		[ScreenPositionMode.TopLeft] = (0, 0),
		[ScreenPositionMode.Top] = (1, 0),
		[ScreenPositionMode.TopRight] = (2, 0),
		[ScreenPositionMode.CenterLeft] = (0, 1),
		[ScreenPositionMode.Center] = (1, 1),
		[ScreenPositionMode.CenterRight] = (2, 1),
		[ScreenPositionMode.BottomLeft] = (0, 2),
		[ScreenPositionMode.BottomCenter] = (1, 2),
		[ScreenPositionMode.BottomRight] = (2, 2),
	};

	/// <summary>
	/// Draws the position visualizer control.
	/// </summary>
	/// <param name="positionMode">The current screen position anchor</param>
	/// <param name="offsetXPercent">X offset from anchor as percentage of screen width (-100 to +100)</param>
	/// <param name="offsetYPercent">Y offset from anchor as percentage of screen height (-100 to +100)</param>
	/// <param name="widthPercent">Overlay width as percentage of screen width (0 to 100)</param>
	/// <param name="heightPercent">Overlay height as percentage of screen height (0 to 100)</param>
	/// <param name="visualizerWidth">Width of the visualizer widget</param>
	/// <returns>The clicked anchor position, or null if no anchor was clicked</returns>
	public static ScreenPositionMode? Draw(ScreenPositionMode positionMode, float offsetXPercent, float offsetYPercent, float widthPercent, float heightPercent, float visualizerWidth = 200f)
	{
		float scale = ImGuiHelpers.GlobalScale;
		float vizWidth = visualizerWidth * scale;
		
		// Use actual screen aspect ratio instead of hardcoded 16:9
		var screen = ImGui.GetMainViewport();
		float aspectRatio = screen.Size.Y / screen.Size.X;
		float vizHeight = vizWidth * aspectRatio;

		Vector2 cursorPos = ImGui.GetCursorScreenPos();
		ImDrawListPtr drawList = ImGui.GetWindowDrawList();

		// Colors
		uint colorBorder = ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 1f));
		uint colorBackground = ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 1f));
		uint colorAnchorInactive = ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1f));
		uint colorAnchorActive = ImGui.GetColorU32(new Vector4(0.2f, 0.8f, 0.2f, 1f));
		uint colorOverlay = ImGui.GetColorU32(new Vector4(0.2f, 0.5f, 1f, 0.8f));
		uint colorOverlayBorder = ImGui.GetColorU32(new Vector4(0.3f, 0.6f, 1f, 1f));

		// Draw background rectangle (represents screen)
		drawList.AddRectFilled(cursorPos, cursorPos + new Vector2(vizWidth, vizHeight), colorBackground);
		drawList.AddRect(cursorPos, cursorPos + new Vector2(vizWidth, vizHeight), colorBorder, 0f, ImDrawFlags.None, 2f);

		float anchorBoxSize = 10f * scale;

		bool isFullscreen = positionMode == ScreenPositionMode.Fullscreen;

		// Draw overlay preview rectangle first (blue box showing where overlay will be)
		// This is drawn before anchor points so anchors appear on top
		if (positionMode != ScreenPositionMode.System)
		{
			// Calculate overlay bounds in visualizer space
			var (overlayMin, overlayMax) = CalculateOverlayBounds(
				positionMode, offsetXPercent, offsetYPercent, widthPercent, heightPercent,
				cursorPos, vizWidth, vizHeight);

			// Clamp to visualizer bounds for display
			Vector2 clampedMin = Vector2.Clamp(overlayMin, cursorPos, cursorPos + new Vector2(vizWidth, vizHeight));
			Vector2 clampedMax = Vector2.Clamp(overlayMax, cursorPos, cursorPos + new Vector2(vizWidth, vizHeight));

			// Only draw if there's a visible area
			if (clampedMax.X > clampedMin.X && clampedMax.Y > clampedMin.Y)
			{
				drawList.AddRectFilled(clampedMin, clampedMax, colorOverlay);
				drawList.AddRect(clampedMin, clampedMax, colorOverlayBorder, 0f, ImDrawFlags.None, 2f);
			}
		}

		// Track clicked anchor
		ScreenPositionMode? clickedPosition = null;

		// Draw anchor points (3x3 grid) - drawn after overlay so they appear on top
		for (int row = 0; row < 3; row++)
		{
			for (int col = 0; col < 3; col++)
			{
				// Calculate position for this anchor box (no padding - flush with border)
				float anchorX = col switch
				{
					0 => cursorPos.X,
					1 => cursorPos.X + (vizWidth - anchorBoxSize) / 2f,
					2 => cursorPos.X + vizWidth - anchorBoxSize,
					_ => cursorPos.X
				};

				float anchorY = row switch
				{
					0 => cursorPos.Y,
					1 => cursorPos.Y + (vizHeight - anchorBoxSize) / 2f,
					2 => cursorPos.Y + vizHeight - anchorBoxSize,
					_ => cursorPos.Y
				};

				Vector2 anchorMin = new(anchorX, anchorY);
				Vector2 anchorMax = anchorMin + new Vector2(anchorBoxSize, anchorBoxSize);

				// Determine if this anchor is active
				bool isActive = isFullscreen;
				if (!isFullscreen && AnchorPositions.TryGetValue(positionMode, out var anchorPos))
				{
					isActive = anchorPos.col == col && anchorPos.row == row;
				}

				uint anchorColor = isActive ? colorAnchorActive : colorAnchorInactive;
				drawList.AddRectFilled(anchorMin, anchorMax, anchorColor);
				drawList.AddRect(anchorMin, anchorMax, colorBorder);

				// Check for click on this anchor box
				if (ImGui.IsMouseHoveringRect(anchorMin, anchorMax) && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
				{
					// Find the ScreenPosition for this grid position
					foreach (var (screenPos, gridPos) in AnchorPositions)
					{
						if (gridPos.col == col && gridPos.row == row)
						{
							clickedPosition = screenPos;
							break;
						}
					}
				}
			}
		}

		// Reserve space for the widget
		ImGui.Dummy(new Vector2(vizWidth, vizHeight));

		return clickedPosition;
	}

	/// <summary>
	/// Calculates the overlay bounds in visualizer space.
	/// All position/size values are percentages (0-100 or -100 to +100 for offsets).
	/// </summary>
	private static (Vector2 min, Vector2 max) CalculateOverlayBounds(
		ScreenPositionMode positionMode, float offsetXPercent, float offsetYPercent, float widthPercent, float heightPercent,
		Vector2 vizOrigin, float vizWidth, float vizHeight)
	{
		// Handle fullscreen
		if (positionMode == ScreenPositionMode.Fullscreen)
		{
			return (vizOrigin, vizOrigin + new Vector2(vizWidth, vizHeight));
		}

		// Convert percentages to visualizer coordinates
		float overlayWidth = vizWidth * (widthPercent / 100f);
		float overlayHeight = vizHeight * (heightPercent / 100f);
		float offsetX = vizWidth * (offsetXPercent / 100f);
		float offsetY = vizHeight * (offsetYPercent / 100f);

		// Get anchor point in visualizer coordinates (0-1 normalized)
		var (anchorNormX, anchorNormY) = GetAnchorPointNormalized(positionMode);
		float anchorX = vizOrigin.X + vizWidth * anchorNormX;
		float anchorY = vizOrigin.Y + vizHeight * anchorNormY;

		// Calculate overlay top-left based on anchor
		// The anchor point is where the overlay's corresponding corner/edge sits
		float overlayLeft = positionMode switch
		{
			ScreenPositionMode.TopLeft or ScreenPositionMode.CenterLeft or ScreenPositionMode.BottomLeft => anchorX + offsetX,
			ScreenPositionMode.Top or ScreenPositionMode.Center or ScreenPositionMode.BottomCenter => anchorX + offsetX - overlayWidth / 2f,
			ScreenPositionMode.TopRight or ScreenPositionMode.CenterRight or ScreenPositionMode.BottomRight => anchorX + offsetX - overlayWidth,
			_ => vizOrigin.X + offsetX
		};

		float overlayTop = positionMode switch
		{
			ScreenPositionMode.TopLeft or ScreenPositionMode.Top or ScreenPositionMode.TopRight => anchorY + offsetY,
			ScreenPositionMode.CenterLeft or ScreenPositionMode.Center or ScreenPositionMode.CenterRight => anchorY + offsetY - overlayHeight / 2f,
			ScreenPositionMode.BottomLeft or ScreenPositionMode.BottomCenter or ScreenPositionMode.BottomRight => anchorY + offsetY - overlayHeight,
			_ => vizOrigin.Y + offsetY
		};

		Vector2 overlayMin = new(overlayLeft, overlayTop);
		Vector2 overlayMax = new(overlayLeft + overlayWidth, overlayTop + overlayHeight);

		return (overlayMin, overlayMax);
	}

	/// <summary>
	/// Gets the anchor point as normalized coordinates (0-1) for a given position.
	/// </summary>
	private static (float x, float y) GetAnchorPointNormalized(ScreenPositionMode positionMode)
	{
		return positionMode switch
		{
			ScreenPositionMode.TopLeft => (0f, 0f),
			ScreenPositionMode.Top => (0.5f, 0f),
			ScreenPositionMode.TopRight => (1f, 0f),
			ScreenPositionMode.CenterLeft => (0f, 0.5f),
			ScreenPositionMode.Center => (0.5f, 0.5f),
			ScreenPositionMode.CenterRight => (1f, 0.5f),
			ScreenPositionMode.BottomLeft => (0f, 1f),
			ScreenPositionMode.BottomCenter => (0.5f, 1f),
			ScreenPositionMode.BottomRight => (1f, 1f),
			_ => (0f, 0f)
		};
	}
}
