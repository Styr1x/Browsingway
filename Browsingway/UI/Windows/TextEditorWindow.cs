using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using System.Numerics;

namespace Browsingway.UI.Windows;

internal class TextEditorWindow : Window
{
	private readonly Action<string> _onSave;
	private readonly Action _onCancel;
	private bool _needsCenter = true;
	public string Code;
	public string Title;

	public TextEditorWindow(string title, string initialCode, Action<string> onSave, Action onCancel)
		: base($"{title}###{Guid.NewGuid()}")
	{
		Title = title;
		_onSave = onSave;
		_onCancel = onCancel;
		Code = initialCode;

		SizeConstraints = new WindowSizeConstraints
		{
			MinimumSize = new Vector2(1000, 750),
			MaximumSize = new Vector2(2500, 2000)
		};

		IsOpen = true;
	}

	public override void PreDraw()
	{
		// Center window on first appearance
		if (_needsCenter)
		{
			var center = ImGui.GetMainViewport().GetCenter();
			ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
			_needsCenter = false;
		}
	}

	public override void Draw()
	{
		float footerHeight = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
		Vector2 contentSize = new Vector2(-1, -footerHeight);

		ImGui.InputTextMultiline("##CodeEditor", ref Code, 1000000, contentSize);

		ImGui.Separator();

		if (ImGui.Button("Save##EditorSave", ImGuiHelpers.ScaledVector2(80, 0)))
		{
			_onSave(Code);
			IsOpen = false;
		}

		ImGui.SameLine();

		if (ImGui.Button("Cancel##EditorCancel", ImGuiHelpers.ScaledVector2(80, 0)))
		{
			_onCancel();
			IsOpen = false;
		}
	}
}

