using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Browsingway.UI.Windows.SettingsTabs;

/// <summary>
/// Self-contained UI component for editing visibility rules.
/// Handles the rules list, conflict detection, reordering, and presets.
/// </summary>
internal static partial class VisibilityRulesEditor
{
	private static readonly Vector4 WarningColor = new(1f, 0.8f, 0f, 1f);
	private static readonly Vector4 HelpTextColor = new(0.4f, 0.4f, 0.4f, 1f);
	private static readonly Vector4 SubtleTextColor = new(0.5f, 0.5f, 0.5f, 1f);

	private static readonly (string Name, VisibilityRule Rule)[] RulePresets =
	[
		("Show in combat", new VisibilityRule { Trigger = VisibilityTrigger.InCombat, Action = VisibilityAction.Show }),
		("Hide out of combat", new VisibilityRule { Negated = true, Trigger = VisibilityTrigger.InCombat, Action = VisibilityAction.Hide }),
		("Show when ACT available", new VisibilityRule { Trigger = VisibilityTrigger.ActAvailable, Action = VisibilityAction.Show }),
		("Hide in PvP", new VisibilityRule { Trigger = VisibilityTrigger.InPvp, Action = VisibilityAction.Hide }),
		("Disable in PvP", new VisibilityRule { Trigger = VisibilityTrigger.InPvp, Action = VisibilityAction.Disable }),
	];

	/// <summary>
	/// Draws the visibility rules editor UI.
	/// </summary>
	/// <param name="rules">The list of rules to edit (modified in place)</param>
	public static void Draw(List<VisibilityRule> rules)
	{
		ImGui.Text("Visibility Rules:");
		ImGuiHelpers.ScaledDummy(2);

		DrawRulesList(rules);
		DrawAddRuleControls(rules);
	}

	private static void DrawRulesList(List<VisibilityRule> rules)
	{
		for (int i = 0; i < rules.Count; i++)
		{
			var rule = rules[i];
			ImGui.PushID($"Rule{i}");

			// Check for conflicts
			var (hasConflict, conflictMessage) = CheckForConflicts(rules, i);

			ImGui.AlignTextToFramePadding();

			// Reorder buttons
			DrawReorderButtons(rules, i);

			ImGui.SameLine();

			// Enabled checkbox
			ImGui.Checkbox("##Enabled", ref rule.Enabled);
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip(rule.Enabled ? "Click to disable this rule" : "Click to enable this rule");

			ImGui.SameLine();

			// Dim the row if disabled
			if (!rule.Enabled)
				ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);

			// Conflict warning
			if (hasConflict)
			{
				ImGui.TextColored(WarningColor, "!");
				if (ImGui.IsItemHovered())
					ImGui.SetTooltip(conflictMessage);
				ImGui.SameLine();
			}

			// Rule controls
			DrawRuleControls(rule);

			if (!rule.Enabled)
				ImGui.PopStyleVar();

			ImGui.SameLine();

			// Remove Button
			if (ImGui.SmallButton("X"))
			{
				rules.RemoveAt(i);
				i--;
				ImGui.PopID();
				continue;
			}
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Remove rule");

			// Summary tooltip
			ImGui.SameLine();
			ImGui.TextColored(HelpTextColor, "?");
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip(GetRuleSummary(rule));

			ImGui.PopID();
		}
	}

	private static void DrawReorderButtons(List<VisibilityRule> rules, int index)
	{
		if (index <= 0)
			ImGui.BeginDisabled();
		if (ImGui.ArrowButton("##Up", ImGuiDir.Up))
		{
			(rules[index], rules[index - 1]) = (rules[index - 1], rules[index]);
		}
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("Move up");
		if (index <= 0)
			ImGui.EndDisabled();

		ImGui.SameLine();

		if (index >= rules.Count - 1)
			ImGui.BeginDisabled();
		if (ImGui.ArrowButton("##Down", ImGuiDir.Down))
		{
			(rules[index], rules[index + 1]) = (rules[index + 1], rules[index]);
		}
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("Move down");
		if (index >= rules.Count - 1)
			ImGui.EndDisabled();
	}

	private static void DrawRuleControls(VisibilityRule rule)
	{
		// Combined If/If NOT dropdown
		ImGui.SetNextItemWidth(70 * ImGuiHelpers.GlobalScale);
		string ifLabel = rule.Negated ? "If NOT" : "If";
		if (ImGui.BeginCombo("##IfNot", ifLabel))
		{
			if (ImGui.Selectable("If", !rule.Negated))
				rule.Negated = false;
			if (ImGui.Selectable("If NOT", rule.Negated))
				rule.Negated = true;
			ImGui.EndCombo();
		}

		ImGui.SameLine();

		// Trigger Dropdown
		ImGui.SetNextItemWidth(120 * ImGuiHelpers.GlobalScale);
		if (ImGui.BeginCombo("##Trigger", SplitCamelCase(rule.Trigger.ToString())))
		{
			foreach (var trigger in Enum.GetValues<VisibilityTrigger>())
			{
				if (ImGui.Selectable(SplitCamelCase(trigger.ToString()), rule.Trigger == trigger))
				{
					rule.Trigger = trigger;
				}
			}
			ImGui.EndCombo();
		}

		ImGui.SameLine();
		ImGui.Text("then");
		ImGui.SameLine();

		// Action Dropdown
		ImGui.SetNextItemWidth(80 * ImGuiHelpers.GlobalScale);
		if (ImGui.BeginCombo("##Action", rule.Action.ToString()))
		{
			foreach (var action in Enum.GetValues<VisibilityAction>())
			{
				if (ImGui.Selectable(action.ToString(), rule.Action == action))
				{
					rule.Action = action;
				}
			}
			ImGui.EndCombo();
		}

		ImGui.SameLine();

		// Delay - show compact toggle or full input
		if (rule.DelaySeconds > 0)
		{
			ImGui.Text("after");
			ImGui.SameLine();
			ImGui.SetNextItemWidth(35 * ImGuiHelpers.GlobalScale);
			ImGui.InputInt("##Delay", ref rule.DelaySeconds, 0, 0);
			if (rule.DelaySeconds < 0) rule.DelaySeconds = 0;
			ImGui.SameLine();
			ImGui.Text("sec");
		}
		else
		{
			ImGui.TextColored(SubtleTextColor, "(+delay)");
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Click to add a delay");
			if (ImGui.IsItemClicked())
				rule.DelaySeconds = 5;
		}
	}

	private static void DrawAddRuleControls(List<VisibilityRule> rules)
	{
		if (ImGui.Button("Add Rule"))
		{
			rules.Add(new VisibilityRule
			{
				Trigger = VisibilityTrigger.InCombat,
				Action = VisibilityAction.Show,
				DelaySeconds = 0
			});
		}

		ImGui.SameLine();

		ImGui.SetNextItemWidth(160 * ImGuiHelpers.GlobalScale);
		if (ImGui.BeginCombo("##Presets", "Add from preset..."))
		{
			foreach (var (name, preset) in RulePresets)
			{
				if (ImGui.Selectable(name))
				{
					rules.Add(new VisibilityRule
					{
						Enabled = preset.Enabled,
						Negated = preset.Negated,
						Trigger = preset.Trigger,
						Action = preset.Action,
						DelaySeconds = preset.DelaySeconds
					});
				}
			}
			ImGui.EndCombo();
		}
	}

	#region Helper Methods

	private static (bool hasConflict, string message) CheckForConflicts(List<VisibilityRule> rules, int currentIndex)
	{
		var current = rules[currentIndex];
		for (int i = 0; i < rules.Count; i++)
		{
			if (i == currentIndex) continue;
			var other = rules[i];

			// Same trigger and negation, but different actions
			if (other.Trigger == current.Trigger && other.Negated == current.Negated)
			{
				bool isOpposite = (current.Action == VisibilityAction.Show && other.Action == VisibilityAction.Hide) ||
								  (current.Action == VisibilityAction.Hide && other.Action == VisibilityAction.Show) ||
								  (current.Action == VisibilityAction.Enable && other.Action == VisibilityAction.Disable) ||
								  (current.Action == VisibilityAction.Disable && other.Action == VisibilityAction.Enable);
				if (isOpposite)
					return (true, $"Conflicts with rule {i + 1}: same condition but opposite action");
			}
		}
		return (false, "");
	}

	private static string GetRuleSummary(VisibilityRule rule)
	{
		string condition = rule.Negated ? "If NOT" : "If";
		string trigger = SplitCamelCase(rule.Trigger.ToString()).ToLower();
		string action = rule.Action.ToString().ToLower();
		string delay = rule.DelaySeconds > 0 ? $" after {rule.DelaySeconds}s" : "";
		return $"{condition} {trigger}, {action} overlay{delay}";
	}

	private static string SplitCamelCase(string input)
	{
		return CamelCaseRegex().Replace(input, " $1");
	}

	[GeneratedRegex(@"(\B[A-Z])")]
	private static partial Regex CamelCaseRegex();

	#endregion
}
