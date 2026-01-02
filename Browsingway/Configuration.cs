using Dalamud.Configuration;

namespace Browsingway;

[Serializable]
internal sealed class Configuration : IPluginConfiguration
{
	// Current version of the configuration schema
	private const int CurrentVersion = 1;

	public int Version { get; set; }

	// General
	public RenderingBackend Backend { get; set; } = RenderingBackend.CEF;

	// IPC
	public bool AllowEphemeralWindows { get; set; } = false;
	public bool AllowConfigChanges { get; set; } = false;

	// legacy config, import only
	public List<InlayConfiguration> Inlays { get; init; } = [];
	public List<OverlayConfiguration> Overlays { get; init; } = [];

	/// <summary>
	/// Defines all configuration migrations in order.
	/// Each migration transforms from version n to version n+1.
	/// </summary>
	private static readonly Dictionary<int, Func<Configuration, bool>> Migrations = new()
	{
		// Version 0 -> 1: Migrate Inlays to Overlays
		[0] = config => MigrateV0ToV1(config),
		// Future migrations can be added here:
		// [1] = config => MigrateV1ToV2(config),
		// [2] = config => MigrateV2ToV3(config),
	};

	/// <summary>
	/// Migrates the configuration to the current version.
	/// Returns true if any migration was performed and config should be saved.
	/// </summary>
	public bool Migrate()
	{
		var migrationPerformed = false;

		// Run all migrations sequentially until we reach the current version
		while (Version < CurrentVersion)
		{
			if (Migrations.TryGetValue(Version, out var migration))
			{
				if (migration(this))
				{
					migrationPerformed = true;
				}
			}
			else
			{
				// If no specific migration exists, just bump the version
				Version++;
				migrationPerformed = true;
			}
		}

		return migrationPerformed;
	}

	/// <summary>
	/// Migration from version 0 to version 1.
	/// Converts legacy Inlay configurations to Overlay configurations.
	/// </summary>
	private static bool MigrateV0ToV1(Configuration config)
	{
		// Migrate Inlays to Overlays if present
		if (config.Inlays.Count > 0)
		{
			foreach (var inlay in config.Inlays)
			{
				var cfg = new OverlayConfiguration
				{
					Guid = inlay.Guid,
					Name = inlay.Name,
					Url = inlay.Url,
					Zoom = inlay.Zoom,
					Opacity = inlay.Opacity,
					Framerate = inlay.Framerate,
					BaseVisibility = inlay.Disabled ? BaseVisibility.Disabled : (inlay.Hidden ? BaseVisibility.Hidden : BaseVisibility.Visible),
					Locked = inlay.Locked,
					Muted = inlay.Muted,
					TypeThrough = inlay.TypeThrough,
					ClickThrough = inlay.ClickThrough,
					Fullscreen = inlay.Fullscreen,
					CustomCss = inlay.CustomCss,
				};

				if (inlay.Disabled)
				{
					cfg.BaseVisibility = BaseVisibility.Disabled;
				}
				else if (inlay.Hidden)
				{
					cfg.BaseVisibility = BaseVisibility.Hidden;
				}

				// Visibility rules
				if (inlay.ActOptimizations)
				{
					cfg.BaseVisibility = BaseVisibility.Disabled;
					cfg.VisibilityRules.Add(new VisibilityRule {Trigger = VisibilityTrigger.ActAvailable, Action = VisibilityAction.Enable});
				}
				
				if (inlay.HideOutOfCombat)
				{
					cfg.VisibilityRules.Add(new VisibilityRule {Negated = true, Trigger = VisibilityTrigger.InCombat, Action = VisibilityAction.Hide, DelaySeconds = 5});
				}

				if (inlay.HideInPvP)
				{
					cfg.VisibilityRules.Add(new VisibilityRule {Trigger = VisibilityTrigger.InPvp, Action = VisibilityAction.Hide});
				}
				
				config.Overlays.Add(cfg);
			}

			config.Inlays.Clear();
		}

		config.Version = 1;
		return true; // Always return true to save the version bump
	}
}

/// <summary>
/// Legacy configuration, only used for importing old config files
/// </summary>
[Serializable]
#pragma warning disable 0649
internal sealed class InlayConfiguration
{
	public Guid Guid = Guid.NewGuid();
	public string Name = "New overlay";
	public string Url = "about:blank";

	// Rendering
	public float Zoom = 100f;
	public float Opacity = 100f;
	public int Framerate = 60;

	// Behavior flags
	public bool Disabled;
	public bool Hidden;
	public bool Locked;
	public bool Muted;
	public bool TypeThrough;
	public bool ClickThrough;
	public bool Fullscreen;

	// Combat/PvP visibility
	public bool ActOptimizations;
	public bool HideOutOfCombat;
	public bool HideInPvP;
	public int HideDelay;

	// Advanced
	public string CustomCss = "";
}
#pragma warning restore

public enum VisibilityTrigger
{
	ActAvailable,
	InCombat,
	InPvp
}

public enum VisibilityAction
{
	Hide,
	Disable,
	Enable,
	Show
}

public enum RenderingBackend
{
	CEF
}

public enum BaseVisibility
{
	Visible,
	Hidden,
	Disabled
}

[Serializable]
public sealed class VisibilityRule
{
	public bool Enabled = true;
	public bool Negated;
	public VisibilityTrigger Trigger;
	public VisibilityAction Action;
	public int DelaySeconds;
}

/// <summary>
/// Configuration for a single overlay.
/// Note: Fields are used instead of properties because ImGui bindings require ref parameters.
/// </summary>
[Serializable]
internal sealed class OverlayConfiguration
{
	public Guid Guid = Guid.NewGuid();
	public string Name = "New overlay";
	public string Url = "about:blank";

	// Rendering
	public float Zoom = 100f;
	public float Opacity = 100f;
	public int Framerate = 60;

	// Behavior flags
	public BaseVisibility BaseVisibility;
	public bool Locked;
	public bool Muted;
	public bool TypeThrough;
	public bool ClickThrough;
	public bool Fullscreen;

	// Advanced
	public string CustomCss = "";
	public string CustomJs = "";

	public List<VisibilityRule> VisibilityRules = [];
}