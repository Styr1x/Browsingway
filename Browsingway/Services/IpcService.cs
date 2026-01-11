using Dalamud.Plugin.Ipc;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Browsingway.Services;

/// <summary>
/// Dalamud IPC endpoints for other plugins. See IPC_API.md for documentation.
/// </summary>
internal sealed class IpcService : IDisposable
{
	private const string IpcPrefix = "Browsingway";
	private const int ApiVersion = 1;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		WriteIndented = false
	};

	private readonly ServiceContainer _services;
	private readonly Func<Configuration> _getConfig;
	private readonly Action _saveConfig;

	// TODO: Wire to OverlayManager when available
	// private readonly OverlayManager _overlayManager;

	private readonly ICallGateProvider<string> _getInfo;
	private readonly ICallGateProvider<string, string> _overlayCreate;
	private readonly ICallGateProvider<string, string> _overlayRemove;
	private readonly ICallGateProvider<string, string> _overlayControl;
	private readonly ICallGateProvider<string, string> _configAdd;

	public IpcService(ServiceContainer services, Func<Configuration> getConfig, Action saveConfig)
	{
		_services = services;
		_getConfig = getConfig;
		_saveConfig = saveConfig;

		_getInfo = RegisterProvider<string>("GetInfo");
		_overlayCreate = RegisterProvider<string, string>("Overlay.Create");
		_overlayRemove = RegisterProvider<string, string>("Overlay.Remove");
		_overlayControl = RegisterProvider<string, string>("Overlay.Control");
		_configAdd = RegisterProvider<string, string>("Config.Add");

		_getInfo.RegisterFunc(HandleGetInfo);
		_overlayCreate.RegisterFunc(HandleOverlayCreate);
		_overlayRemove.RegisterFunc(HandleOverlayRemove);
		_overlayControl.RegisterFunc(HandleOverlayControl);
		_configAdd.RegisterFunc(HandleConfigAdd);
	}

	public void Dispose()
	{
		_getInfo.UnregisterFunc();
		_overlayCreate.UnregisterFunc();
		_overlayRemove.UnregisterFunc();
		_overlayControl.UnregisterFunc();
		_configAdd.UnregisterFunc();
	}

	private ICallGateProvider<TRet> RegisterProvider<TRet>(string name)
		=> _services.PluginInterface.GetIpcProvider<TRet>($"{IpcPrefix}.{name}");

	private ICallGateProvider<T1, TRet> RegisterProvider<T1, TRet>(string name)
		=> _services.PluginInterface.GetIpcProvider<T1, TRet>($"{IpcPrefix}.{name}");

	#region Handlers

	private string HandleGetInfo()
	{
		var config = _getConfig();
		return JsonSerializer.Serialize(new
		{
			ApiVersion,
			CanCreateEphemeralOverlays = config.AllowEphemeralWindows,
			CanAddOverlaysToConfig = config.AllowConfigChanges
		}, JsonOptions);
	}

	private string HandleOverlayCreate(string requestJson)
	{
		try
		{
			var config = _getConfig();
			if (!config.AllowEphemeralWindows)
				return ErrorResponse("Ephemeral overlays are disabled in settings");

			var request = JsonSerializer.Deserialize<OverlayCreateRequest>(requestJson, JsonOptions);
			if (request == null || string.IsNullOrWhiteSpace(request.Url))
				return ErrorResponse("URL is required");

			// TODO: Wire to OverlayManager.AddEphemeralOverlay
			// var overlayConfig = new OverlayConfiguration
			// {
			//     Url = request.Url,
			//     PositionMode = ParsePositionMode(request.ScreenPositionMode),
			//     PositionX = request.X ?? 0,
			//     PositionY = request.Y ?? 0,
			//     PositionWidth = request.Width ?? 50,
			//     PositionHeight = request.Height ?? 50,
			//     Opacity = request.Opacity ?? 100,
			//     Zoom = request.Zoom ?? 100,
			//     Muted = request.Muted ?? false,
			//     ClickThrough = request.ClickThrough ?? false,
			//     CustomCss = request.CustomCss ?? "",
			//     CustomJs = request.CustomJs ?? ""
			// };
			// var guid = _overlayManager.AddEphemeralOverlay(overlayConfig);

			var guid = Guid.NewGuid();
			return SuccessResponse(new { Guid = guid.ToString() });
		}
		catch (JsonException ex)
		{
			return ErrorResponse($"JSON parse error: {ex.Message}");
		}
	}

	private string HandleOverlayRemove(string requestJson)
	{
		try
		{
			var request = JsonSerializer.Deserialize<GuidRequest>(requestJson, JsonOptions);
			if (request == null || string.IsNullOrWhiteSpace(request.Guid))
				return ErrorResponse("GUID is required");

			if (!Guid.TryParse(request.Guid, out var guid))
				return ErrorResponse("Invalid GUID format");

			// TODO: Wire to OverlayManager.RemoveEphemeralOverlay(guid)
			// if (!_overlayManager.IsEphemeral(guid))
			//     return ErrorResponse("Overlay not found or is not ephemeral");
			// _overlayManager.RemoveEphemeralOverlay(guid);

			return SuccessResponse();
		}
		catch (JsonException ex)
		{
			return ErrorResponse($"JSON parse error: {ex.Message}");
		}
	}

	private string HandleOverlayControl(string requestJson)
	{
		try
		{
			var request = JsonSerializer.Deserialize<OverlayControlRequest>(requestJson, JsonOptions);
			if (request == null || string.IsNullOrWhiteSpace(request.Guid))
				return ErrorResponse("GUID is required");

			if (!Guid.TryParse(request.Guid, out var guid))
				return ErrorResponse("Invalid GUID format");

			// TODO: Wire to OverlayManager/RenderProcessManager
			// if (!_overlayManager.HasOverlay(guid))
			//     return ErrorResponse("Overlay not found");

			if (!string.IsNullOrEmpty(request.Navigate))
			{
				// TODO: _overlayManager.NavigateOverlay(guid, request.Navigate);
			}

			if (request.Reload == true)
			{
				// TODO: _overlayManager.ReloadOverlay(guid);
			}

			if (!string.IsNullOrEmpty(request.InjectCss))
			{
				// TODO: _renderProcessManager.Rpc?.InjectCss(guid, request.InjectCss);
			}

			if (!string.IsNullOrEmpty(request.ExecuteJs))
			{
				// TODO: _renderProcessManager.Rpc?.ExecuteJs(guid, request.ExecuteJs);
			}

			if (!string.IsNullOrEmpty(request.SetVisibility))
			{
				// TODO: Handle "hide" / "show"
				// var visibility = request.SetVisibility.ToLowerInvariant() switch
				// {
				//     "hide" => BaseVisibility.Hidden,
				//     "show" => BaseVisibility.Visible,
				//     _ => (BaseVisibility?)null
				// };
				// if (visibility == null)
				//     return ErrorResponse("setVisibility must be 'hide' or 'show'");
			}

			return SuccessResponse();
		}
		catch (JsonException ex)
		{
			return ErrorResponse($"JSON parse error: {ex.Message}");
		}
	}

	private string HandleConfigAdd(string requestJson)
	{
		try
		{
			var pluginConfig = _getConfig();
			if (!pluginConfig.AllowConfigChanges)
				return ErrorResponse("Config changes are disabled in settings");

			var request = JsonSerializer.Deserialize<ConfigAddRequest>(requestJson, JsonOptions);
			if (request == null || string.IsNullOrWhiteSpace(request.Url) || string.IsNullOrWhiteSpace(request.Name))
				return ErrorResponse("Name and URL are required");

			// Check for duplicate name
			if (pluginConfig.Overlays.Any(o => o.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase)))
				return ErrorResponse($"An overlay with name '{request.Name}' already exists");

			var overlayConfig = new OverlayConfiguration
			{
				Name = request.Name,
				Url = request.Url,
				PositionMode = ParsePositionMode(request.ScreenPositionMode),
				PositionX = request.X ?? 0,
				PositionY = request.Y ?? 0,
				PositionWidth = request.Width ?? 50,
				PositionHeight = request.Height ?? 50,
				Opacity = request.Opacity ?? 100,
				Zoom = request.Zoom ?? 100,
				Muted = request.Muted ?? false,
				Locked = request.Locked ?? false,
				TypeThrough = request.TypeThrough ?? false,
				ClickThrough = request.ClickThrough ?? false,
				BaseVisibility = ParseVisibility(request.BaseVisibility),
				CustomCss = request.CustomCss ?? "",
				CustomJs = request.CustomJs ?? ""
			};

			// Parse visibility rules
			if (request.VisibilityRules != null)
			{
				foreach (var rule in request.VisibilityRules)
				{
					var trigger = rule.Trigger?.ToLowerInvariant() switch
					{
						"actavailable" => VisibilityTrigger.ActAvailable,
						"incombat" => VisibilityTrigger.InCombat,
						"inpvp" => VisibilityTrigger.InPvp,
						_ => (VisibilityTrigger?)null
					};
					var action = rule.Action?.ToLowerInvariant() switch
					{
						"show" => VisibilityAction.Show,
						"hide" => VisibilityAction.Hide,
						"enable" => VisibilityAction.Enable,
						"disable" => VisibilityAction.Disable,
						_ => (VisibilityAction?)null
					};

					if (trigger == null || action == null)
						continue;

					overlayConfig.VisibilityRules.Add(new VisibilityRule
					{
						Enabled = rule.Enabled ?? true,
						Negated = rule.Negated ?? false,
						Trigger = trigger.Value,
						Action = action.Value,
						DelaySeconds = rule.Delay ?? 0
					});
				}
			}

			pluginConfig.Overlays.Add(overlayConfig);
			_saveConfig();

			// TODO: Wire to OverlayManager.AddOrUpdateOverlay(overlayConfig)
			// TODO: Open settings window and select new overlay

			return SuccessResponse(new { Guid = overlayConfig.Guid.ToString() });
		}
		catch (JsonException ex)
		{
			return ErrorResponse($"JSON parse error: {ex.Message}");
		}
	}

	#endregion

	#region Helpers

	private static ScreenPositionMode ParsePositionMode(string? mode) => mode?.ToLowerInvariant() switch
	{
		"fullscreen" => ScreenPositionMode.Fullscreen,
		"topleft" => ScreenPositionMode.TopLeft,
		"top" => ScreenPositionMode.Top,
		"topright" => ScreenPositionMode.TopRight,
		"centerleft" => ScreenPositionMode.CenterLeft,
		"center" => ScreenPositionMode.Center,
		"centerright" => ScreenPositionMode.CenterRight,
		"bottomleft" => ScreenPositionMode.BottomLeft,
		"bottomcenter" => ScreenPositionMode.BottomCenter,
		"bottomright" => ScreenPositionMode.BottomRight,
		_ => ScreenPositionMode.System
	};

	private static BaseVisibility ParseVisibility(string? visibility) => visibility?.ToLowerInvariant() switch
	{
		"hidden" => BaseVisibility.Hidden,
		"disabled" => BaseVisibility.Disabled,
		_ => BaseVisibility.Visible
	};

	private static string SuccessResponse(object? data = null)
	{
		if (data == null)
			return JsonSerializer.Serialize(new { Success = true }, JsonOptions);

		var dict = new Dictionary<string, object> { ["success"] = true };
		foreach (var prop in data.GetType().GetProperties())
		{
			var value = prop.GetValue(data);
			if (value != null)
				dict[JsonNamingPolicy.CamelCase.ConvertName(prop.Name)] = value;
		}
		return JsonSerializer.Serialize(dict, JsonOptions);
	}

	private static string ErrorResponse(string error)
		=> JsonSerializer.Serialize(new { Success = false, Error = error }, JsonOptions);

	#endregion

	#region DTOs

	private sealed class GuidRequest
	{
		public string? Guid { get; set; }
	}

	private sealed class OverlayCreateRequest
	{
		public string? Url { get; set; }
		public string? ScreenPositionMode { get; set; }
		public float? X { get; set; }
		public float? Y { get; set; }
		public float? Width { get; set; }
		public float? Height { get; set; }
		public float? Opacity { get; set; }
		public float? Zoom { get; set; }
		public bool? Muted { get; set; }
		public bool? ClickThrough { get; set; }
		public string? CustomCss { get; set; }
		public string? CustomJs { get; set; }
	}

	private sealed class OverlayControlRequest
	{
		public string? Guid { get; set; }
		public string? Navigate { get; set; }
		public bool? Reload { get; set; }
		public string? InjectCss { get; set; }
		public string? ExecuteJs { get; set; }
		public string? SetVisibility { get; set; }
	}

	private sealed class ConfigAddRequest
	{
		public string? Name { get; set; }
		public string? Url { get; set; }
		public string? ScreenPositionMode { get; set; }
		public float? X { get; set; }
		public float? Y { get; set; }
		public float? Width { get; set; }
		public float? Height { get; set; }
		public float? Opacity { get; set; }
		public float? Zoom { get; set; }
		public bool? Muted { get; set; }
		public bool? Locked { get; set; }
		public bool? TypeThrough { get; set; }
		public bool? ClickThrough { get; set; }
		public string? BaseVisibility { get; set; }
		public List<VisibilityRuleRequest>? VisibilityRules { get; set; }
		public string? CustomCss { get; set; }
		public string? CustomJs { get; set; }
	}

	private sealed class VisibilityRuleRequest
	{
		public string? Trigger { get; set; }
		public string? Action { get; set; }
		public bool? Enabled { get; set; }
		public bool? Negated { get; set; }
		public int? Delay { get; set; }
	}

	#endregion
}
