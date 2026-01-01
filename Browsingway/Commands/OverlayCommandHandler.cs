using Browsingway.Services;
using System.Text.RegularExpressions;

namespace Browsingway.Commands;

/// <summary>
/// Handles overlay-related chat commands.
/// </summary>
internal sealed partial class OverlayCommandHandler
{
	[GeneratedRegex(@"\s+")]
	private static partial Regex WhitespaceRegex();

	private readonly IServiceContainer _services;
	private readonly IOverlayManager _overlayManager;
	private readonly Func<Configuration> _getConfig;
	private readonly Action _saveConfig;

	public OverlayCommandHandler(
		IServiceContainer services,
		IOverlayManager overlayManager,
		Func<Configuration> getConfig,
		Action saveConfig)
	{
		_services = services;
		_overlayManager = overlayManager;
		_getConfig = getConfig;
		_saveConfig = saveConfig;
	}

	/// <summary>
	/// Handles overlay commands from chat.
	/// </summary>
	public void Handle(string rawArgs)
	{
		string[] args = rawArgs.Split(null as char[], 3, StringSplitOptions.RemoveEmptyEntries);

		if (args.Length < 2 || (args[1] != "reload" && args.Length < 3))
		{
			_services.Chat.PrintError("Invalid overlay command. Supported syntax: '[overlayCommandName] [setting] [value]'");
			return;
		}

		var config = _getConfig();
		string commandName = args[0];
		var targetConfig = config.Overlays.Find(o =>
			WhitespaceRegex().Replace(o.Name, "").Equals(commandName, StringComparison.OrdinalIgnoreCase));

		if (targetConfig == null)
		{
			_services.Chat.PrintError($"Unknown overlay '{args[0]}'.");
			return;
		}

		bool handled = true;
		bool needsReload = false;

		switch (args[1])
		{
			case "url":
				targetConfig.Url = args[2];
				needsReload = true;
				break;
			case "locked":
				handled = TrySetBoolean(args[2], ref targetConfig.Locked);
				break;
			case "visibility":
				handled = TrySetVisibility(args[2], targetConfig, out needsReload);
				break;
			case "typethrough":
				handled = TrySetBoolean(args[2], ref targetConfig.TypeThrough);
				break;
			case "fullscreen":
				handled = TrySetBoolean(args[2], ref targetConfig.Fullscreen);
				break;
			case "clickthrough":
				handled = TrySetBoolean(args[2], ref targetConfig.ClickThrough);
				break;
			case "muted":
				handled = TrySetBoolean(args[2], ref targetConfig.Muted);
				if (handled)
					_overlayManager.SetMuted(targetConfig.Guid, targetConfig.Muted);
				break;
			case "reload":
				_overlayManager.NavigateOverlay(targetConfig.Guid, targetConfig.Url);
				return; // Don't save for reload
			default:
				_services.Chat.PrintError(
					$"Unknown setting '{args[1]}'. Valid settings are: url,visibility,locked,fullscreen,clickthrough,typethrough,muted.");
				return;
		}

		if (handled)
		{
			_saveConfig();

			if (needsReload)
			{
				_overlayManager.NavigateOverlay(targetConfig.Guid, targetConfig.Url);
			}
		}
	}

	private bool TrySetBoolean(string value, ref bool target)
	{
		switch (value)
		{
			case "on":
				target = true;
				return true;
			case "off":
				target = false;
				return true;
			case "toggle":
				target = !target;
				return true;
			default:
				_services.Chat.PrintError($"Unknown boolean value '{value}'. Valid values are: on,off,toggle.");
				return false;
		}
	}

	private bool TrySetVisibility(string value, OverlayConfiguration config, out bool needsReload)
	{
		needsReload = true;
		switch (value.ToLowerInvariant())
		{
			case "visible":
				config.BaseVisibility = BaseVisibility.Visible;
				return true;
			case "hidden":
				config.BaseVisibility = BaseVisibility.Hidden;
				return true;
			case "disabled":
				config.BaseVisibility = BaseVisibility.Disabled;
				return true;
			default:
				_services.Chat.PrintError($"Unknown visibility value '{value}'. Valid values are: visible,hidden,disabled.");
				needsReload = false;
				return false;
		}
	}
}
