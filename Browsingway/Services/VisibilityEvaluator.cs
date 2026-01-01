using Browsingway;

namespace Browsingway.Services;

/// <summary>
/// Environment state used for evaluating visibility rules.
/// Provides current state and timing information for each condition.
/// </summary>
public sealed record GameEnvironment
{
	/// <summary>
	/// Whether ACT/IINACT is currently running.
	/// </summary>
	public bool IsActAvailable { get; init; }

	/// <summary>
	/// Seconds since ACT availability state last changed.
	/// </summary>
	public int SecondsSinceActChanged { get; init; }

	/// <summary>
	/// Whether the player is currently in combat.
	/// </summary>
	public bool IsInCombat { get; init; }

	/// <summary>
	/// Seconds since combat state last changed.
	/// </summary>
	public int SecondsSinceCombatChanged { get; init; }

	/// <summary>
	/// Whether the player is currently in PvP.
	/// </summary>
	public bool IsInPvP { get; init; }

	/// <summary>
	/// Seconds since PvP state last changed.
	/// </summary>
	public int SecondsSincePvPChanged { get; init; }
}

/// <summary>
/// Service that evaluates visibility rules to determine the effective visibility state.
/// </summary>
public static class VisibilityEvaluator
{
	/// <summary>
	/// Computes the effective visibility state for an overlay.
	/// Rules are evaluated in order; later rules can override earlier ones.
	/// </summary>
	/// <param name="baseVisibility">The base visibility state of the overlay.</param>
	/// <param name="rules">The list of visibility rules to evaluate.</param>
	/// <param name="environment">The current environment state.</param>
	/// <returns>The computed visibility state.</returns>
	public static BaseVisibility ComputeVisibility(
		BaseVisibility baseVisibility,
		IReadOnlyList<VisibilityRule> rules,
		GameEnvironment environment)
	{
		var currentVisibility = baseVisibility;

		foreach (var rule in rules)
		{
			if (!rule.Enabled)
				continue;

			bool conditionMet = EvaluateCondition(rule, environment);

			if (conditionMet)
			{
				currentVisibility = ApplyAction(currentVisibility, rule.Action);
			}
		}

		return currentVisibility;
	}

	/// <summary>
	/// Evaluates whether a rule's condition is met.
	/// </summary>
	private static bool EvaluateCondition(VisibilityRule rule, GameEnvironment environment)
	{
		bool triggerState = rule.Trigger switch
		{
			VisibilityTrigger.ActAvailable => environment.IsActAvailable,
			VisibilityTrigger.InCombat => environment.IsInCombat,
			VisibilityTrigger.InPvp => environment.IsInPvP,
			_ => false
		};

		int secondsSinceChanged = rule.Trigger switch
		{
			VisibilityTrigger.ActAvailable => environment.SecondsSinceActChanged,
			VisibilityTrigger.InCombat => environment.SecondsSinceCombatChanged,
			VisibilityTrigger.InPvp => environment.SecondsSincePvPChanged,
			_ => 0
		};

		// Apply negation
		bool conditionMet = rule.Negated ? !triggerState : triggerState;

		// Check delay - only apply rule if enough time has passed since the condition became true
		if (conditionMet && rule.DelaySeconds > 0)
		{
			conditionMet = secondsSinceChanged >= rule.DelaySeconds;
		}

		return conditionMet;
	}

	/// <summary>
	/// Applies a visibility action to the current state.
	/// </summary>
	private static BaseVisibility ApplyAction(BaseVisibility current, VisibilityAction action)
	{
		return action switch
		{
			VisibilityAction.Show => BaseVisibility.Visible,
			VisibilityAction.Hide => BaseVisibility.Hidden,
			VisibilityAction.Enable => current == BaseVisibility.Disabled ? BaseVisibility.Visible : current,
			VisibilityAction.Disable => BaseVisibility.Disabled,
			_ => current
		};
	}
}
