using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using System.Diagnostics;

namespace Browsingway.Services;

/// <summary>
/// Tracks game state changes and provides timing information for visibility rules.
/// Uses Dalamud's Framework.Update for periodic checks (throttled to 500ms).
/// </summary>
internal sealed class GameEnvTracker : IDisposable
{
	private const int UpdateIntervalMs = 500;

	private readonly ServiceContainer _services;
	private readonly ActManager _actManager;
	private readonly Stopwatch _updateTimer = Stopwatch.StartNew();

	// State tracking
	private bool _lastActAvailable;
	private bool _lastInCombat;
	private bool _lastInPvP;

	// Timestamps of last state changes (in ticks)
	private long _actChangedAt;
	private long _combatChangedAt;
	private long _pvpChangedAt;

	/// <summary>
	/// Event fired when the visibility environment changes.
	/// Listeners should recompute overlay visibility.
	/// </summary>
	public event EventHandler<GameEnvironment>? EnvironmentChanged;

	public GameEnvTracker(ServiceContainer services, ActManager actManager)
	{
		_services = services;
		_actManager = actManager;

		// Subscribe to Framework.Update for periodic checks
		_services.Framework.Update += OnFrameworkUpdate;
	}

	public void Dispose()
	{
		_services.Framework.Update -= OnFrameworkUpdate;
	}

	public GameEnvironment CurrentEnvironment { get; private set; } = new();

	private void OnFrameworkUpdate(IFramework framework)
	{
		// Throttle updates to reduce load
		if (_updateTimer.ElapsedMilliseconds < UpdateIntervalMs)
			return;

		_updateTimer.Restart();

		long now = DateTime.UtcNow.Ticks;

		// Check ACT availability
		bool actAvailable = _actManager.IsRunning;
		if (actAvailable != _lastActAvailable)
		{
			_lastActAvailable = actAvailable;
			_actChangedAt = now;
		}

		// Check combat state
		bool inCombat = _services.Condition[ConditionFlag.InCombat];
		if (inCombat != _lastInCombat)
		{
			_lastInCombat = inCombat;
			_combatChangedAt = now;
		}

		// Check PvP state
		bool inPvP = _services.ClientState.IsPvP;
		if (inPvP != _lastInPvP)
		{
			_lastInPvP = inPvP;
			_pvpChangedAt = now;
		}

		// Update environment
		CurrentEnvironment = new GameEnvironment
		{
			IsActAvailable = actAvailable,
			SecondsSinceActChanged = (int)TimeSpan.FromTicks(now - _actChangedAt).TotalSeconds,
			IsInCombat = inCombat,
			SecondsSinceCombatChanged = (int)TimeSpan.FromTicks(now - _combatChangedAt).TotalSeconds,
			IsInPvP = inPvP,
			SecondsSincePvPChanged = (int)TimeSpan.FromTicks(now - _pvpChangedAt).TotalSeconds
		};

		// Always fire event on each update so delay-based visibility rules get re-evaluated.
		// Without this, rules like "hide 5 seconds after leaving combat" would never trigger
		// because the event only fired on state change (when delay was 0).
		EnvironmentChanged?.Invoke(this, CurrentEnvironment);
	}
}

