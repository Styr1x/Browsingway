using Browsingway.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using System.Numerics;

namespace Browsingway.UI.Windows;

internal sealed class DependencyWindow : Window
{
	private const uint ColorProgress = 0xAAD76B39;
	private const uint ColorError = 0xAA0000FF;
	private const uint ColorDone = 0xAA355506;

	private readonly DependencyManager _dependencyManager;
	private readonly ISharedImmediateTexture? _texIcon;

	public DependencyWindow(DependencyManager dependencyManager, IServiceContainer services, string pluginDir)
		: base("Browsingway dependencies###BrowsingwayDependencies")
	{
		_dependencyManager = dependencyManager;
		_texIcon = services.TextureProvider.GetFromFile(Path.Combine(pluginDir, "icon.png"));

		Flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize;

		// Subscribe to manager events to control visibility
		_dependencyManager.StateChanged += OnStateChanged;
	}

	public void Dispose()
	{
		_dependencyManager.StateChanged -= OnStateChanged;
	}

	private void OnStateChanged(object? sender, EventArgs e)
	{
		IsOpen = _dependencyManager.State != DependencyManager.ViewMode.Hidden;
	}

	public override void PreDraw()
	{
		// Center window on screen
		var center = ImGui.GetMainViewport().GetCenter();
		ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
	}

	public override void Draw()
	{
		if (_texIcon is not null)
			ImGui.Image(_texIcon.GetWrapOrEmpty().Handle, ImGuiHelpers.ScaledVector2(128, 128));

		ImGui.SameLine();

		string version = _dependencyManager.MissingDependencies?.FirstOrDefault()?.Version ?? "???";
		string checksum = _dependencyManager.MissingDependencies?.FirstOrDefault()?.Checksum ?? "???";
		ImGui.Text("Browsingway requires additional dependencies to function.\n" +
				   "These are not shipped with the plugin due to their size.\n\n" +
				   "The files are hosted on GitHub and are verified with SHA256 checksums:\n" +
				   "https://github.com/Styr1x/Browsingway/releases/tag/cef-binaries\n\n" +
				   "CefSharp: " + version + "\n" +
				   "SHA256: " + checksum
		);

		switch (_dependencyManager.State)
		{
			case DependencyManager.ViewMode.Confirm:
				RenderConfirm();
				break;
			case DependencyManager.ViewMode.Installing:
				RenderInstalling();
				break;
			case DependencyManager.ViewMode.Complete:
				RenderComplete();
				break;
			case DependencyManager.ViewMode.Failed:
				RenderFailed();
				break;
		}
	}

	private void RenderConfirm()
	{
		if (_dependencyManager.MissingDependencies == null) { return; }

		ImGui.Separator();
		if (ImGui.Button("Install missing dependencies")) { _dependencyManager.InstallDependencies(); }
	}

	private void RenderInstalling()
	{
		ImGui.Text("Installing dependencies: ");
		ImGui.SameLine();
		RenderDownloadProgress();
	}

	private void RenderComplete()
	{
		ImGui.Text("Installing dependencies: ");
		ImGui.SameLine();
		RenderDownloadProgress();
		ImGui.SameLine();
		if (ImGui.Button("Close", ImGuiHelpers.ScaledVector2(100, 0))) { _dependencyManager.CheckDependencies(); }
	}

	private void RenderFailed()
	{
		ImGui.Text("Installing dependencies: ");
		ImGui.SameLine();
		RenderDownloadProgress();
		ImGui.SameLine();
		if (ImGui.Button("Retry", ImGuiHelpers.ScaledVector2(100, 0))) { _dependencyManager.CheckDependencies(); }
	}

	private void RenderDownloadProgress()
	{
		Vector2 progressSize = ImGuiHelpers.ScaledVector2(400, 0);

		foreach (var progress in _dependencyManager.InstallProgress)
		{
			uint color;
			string? label = null;
			float value;

			switch (progress.Value)
			{
				case DependencyManager.DepExtracting:
					color = ColorProgress;
					label = "Extracting";
					value = 1;
					break;
				case DependencyManager.DepComplete:
					color = ColorDone;
					label = "Complete";
					value = 1;
					break;
				case DependencyManager.DepFailed:
					color = ColorError;
					label = "Error";
					value = 1;
					break;
				default:
					color = ColorProgress;
					value = progress.Value / 100;
					break;
			}

			using (ImRaii.PushColor(ImGuiCol.PlotHistogram, color))
			{
				if (label != null)
					ImGui.ProgressBar(value, progressSize, label);
				else
					ImGui.ProgressBar(value, progressSize);
			}
		}
	}
}

