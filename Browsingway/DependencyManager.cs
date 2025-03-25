using Dalamud.Interface.Internal;
using Dalamud.Interface.Textures;
using ImGuiNET;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Browsingway;

internal class Dependency
{
	public readonly string Checksum;
	public readonly string Directory;
	public readonly string Url;
	public readonly string Version;

	public Dependency(string url, string directory, string version, string checksum)
	{
		Directory = directory;
		Url = url;
		Version = version;
		Checksum = checksum;
	}
}

public class DependencyManager : IDisposable
{
	private const string _downloadDir = "downloads";

	private const uint _colorProgress = 0xAAD76B39;
	private const uint _colorError = 0xAA0000FF;
	private const uint _colorDone = 0xAA355506;

	// Per-dependency special-cased progress values
	private const short _depExtracting = -1;
	private const short _depComplete = -2;
	private const short _depFailed = -3;

	private static readonly Dependency[] _dependencies =
	{
		new("https://github.com/Styr1x/Browsingway/releases/download/cef-binaries/cefsharp-{VERSION}.zip", "cef",
			"134.3.6+g96006d1+chromium-134.0.6998.118",
			"BAB1D8237173BDD8DA01E2DB2F639288EB8FE08B033EE9AB8F3520C48ECECC30")
	};

	private readonly string _debugCheckDir;

	private readonly string _dependencyDir;
	private readonly ConcurrentDictionary<string, float> _installProgress = new();
	private Dependency[]? _missingDependencies;
	private ViewMode _viewMode = ViewMode.Hidden;
	private ISharedImmediateTexture? _texIcon;

	public DependencyManager(string pluginDir, string pluginConfigDir)
	{
		_dependencyDir = Path.Join(pluginConfigDir, "dependencies");
		_debugCheckDir = Path.GetDirectoryName(pluginDir) ?? pluginDir;
		_texIcon = Services.TextureProvider.GetFromFile(Path.Combine(pluginDir, "icon.png"));
	}

	public void Dispose() { }

	public event EventHandler? DependenciesReady;

	public void Initialise()
	{
		CheckDependencies();
	}

	private void CheckDependencies()
	{
		_missingDependencies = _dependencies.Where(DependencyMissing).ToArray();
		if (_missingDependencies.Length == 0)
		{
			_viewMode = ViewMode.Hidden;
			DependenciesReady?.Invoke(this, EventArgs.Empty);
		}
		else
		{
			_viewMode = ViewMode.Confirm;
		}
	}

	private bool DependencyMissing(Dependency dependency)
	{
		string versionFilePath = Path.Combine(GetDependencyPath(dependency), "VERSION");

		string versionContents;
		try { versionContents = File.ReadAllText(versionFilePath); }
		catch { return true; }

		return !versionContents.Contains(dependency.Version);
	}

	private void InstallDependencies()
	{
		if (_missingDependencies is null)
		{
			return; // nothing too do
		}

		_viewMode = ViewMode.Installing;
		Services.PluginLog.Info("Installing dependencies...");

		IEnumerable<Task> installTasks = _missingDependencies.Select(InstallDependency);
		Task.WhenAll(installTasks).ContinueWith(task =>
		{
			bool failed = _installProgress.Any(pair => pair.Value == _depFailed);
			_viewMode = failed ? ViewMode.Failed : ViewMode.Complete;
			Services.PluginLog.Info($"Dependency install {_viewMode}.");

			try { Directory.Delete(Path.Combine(_dependencyDir, _downloadDir), true); }
			catch { }
		});
	}

	private async Task InstallDependency(Dependency dependency)
	{
		Services.PluginLog.Info($"Downloading {dependency.Directory} {dependency.Version}");

		// Ensure the downloads dir exists
		string downloadDir = Path.Combine(_dependencyDir, _downloadDir);
		Directory.CreateDirectory(downloadDir);

		// Get the file name we'll download to - if it's already in downloads, it may be corrupt, delete
		string filePath = Path.Combine(downloadDir, $"{dependency.Directory}-{dependency.Version}.zip");
		File.Delete(filePath);

		// Set up the download and kick it off
#pragma warning disable SYSLIB0014
		using WebClient client = new();
#pragma warning restore SYSLIB0014
		client.DownloadProgressChanged += (sender, args) => _installProgress.AddOrUpdate(
			dependency.Directory,
			args.ProgressPercentage,
			(key, oldValue) => Math.Max(oldValue, args.ProgressPercentage));
		await client.DownloadFileTaskAsync(
			dependency.Url.Replace("{VERSION}", dependency.Version),
			filePath);

		// Download complete, mark as extracting
		_installProgress.AddOrUpdate(dependency.Directory, _depExtracting, (key, oldValue) => _depExtracting);

		// Calculate the checksum for the download
		string downloadedChecksum;
		try
		{
			using (SHA256 sha = SHA256.Create())
			using (FileStream stream = new(filePath, FileMode.Open))
			{
				stream.Position = 0;
				byte[] rawHash = sha.ComputeHash(stream);
				StringBuilder builder = new(rawHash.Length);
				for (int i = 0; i < rawHash.Length; i++) { builder.Append($"{rawHash[i]:X2}"); }

				downloadedChecksum = builder.ToString();
			}
		}
		catch
		{
			Services.PluginLog.Error($"Failed to calculate checksum for {filePath}");
			downloadedChecksum = "FAILED";
		}

		// Make sure the checksum matches
		if (downloadedChecksum != dependency.Checksum)
		{
			Services.PluginLog.Error(
				$"Mismatched checksum for {filePath}: Got {downloadedChecksum} but expected {dependency.Checksum}");
			_installProgress.AddOrUpdate(dependency.Directory, _depFailed, (key, oldValue) => _depFailed);
			File.Delete(filePath);
			return;
		}

		_installProgress.AddOrUpdate(dependency.Directory, _depComplete, (key, oldValue) => _depComplete);

		// Extract to the destination dir
		string destinationDir = GetDependencyPath(dependency);
		try { Directory.Delete(destinationDir, true); }
		catch { }

		ZipFile.ExtractToDirectory(filePath, destinationDir);

		// Clear out the downloaded file now we're done with it
		File.Delete(filePath);
	}

	public string GetDependencyPathFor(string dependencyDir)
	{
		Dependency? dependency = _dependencies.First(dependency => dependency.Directory == dependencyDir);
		if (dependency == null) { throw new Exception($"Unknown dependency {dependencyDir}"); }

		return GetDependencyPath(dependency);
	}

	private string GetDependencyPath(Dependency dependency)
	{
		string localDebug = Path.Combine(_debugCheckDir, dependency.Directory);
		if (Directory.Exists(localDebug)) { return localDebug; }

		return Path.Combine(_dependencyDir, dependency.Directory);
	}

	public void Render()
	{
		if (_viewMode == ViewMode.Hidden) { return; }

		ImGui.SetNextWindowSize(new Vector2(1300, 350), ImGuiCond.Always);
		ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize;
		ImGui.Begin("Browsingway dependencies", windowFlags);
		if (_texIcon is not null)
			ImGui.Image(_texIcon.GetWrapOrEmpty().ImGuiHandle, new Vector2(256, 256));

		ImGui.SameLine();

		string version = _missingDependencies?.First()?.Version ?? "???";
		string checksum = _missingDependencies?.First()?.Checksum ?? "???";
		ImGui.Text("Browsingway requires additional dependencies to function.\n" +
		           "These are not shipped with the plugin due to their size.\n\n" +
		           "The files are hosted on GitHub and are verified with SHA256 checksums:\n" +
		           "https://github.com/Styr1x/Browsingway/releases/tag/cef-binaries\n\n" +
		           "CefSharp: " + version + "\n" +
		           "SHA256: " + checksum
		);
		//ImGui.SetWindowFocus();

		switch (_viewMode)
		{
			case ViewMode.Confirm:
				RenderConfirm();
				break;
			case ViewMode.Installing:
				RenderInstalling();
				break;
			case ViewMode.Complete:
				RenderComplete();
				break;
			case ViewMode.Failed:
				RenderFailed();
				break;
		}

		ImGui.End();
	}

	private void RenderConfirm()
	{
		if (_missingDependencies == null) { return; }

		ImGui.Separator();
		if (ImGui.Button("Install missing dependencies")) { InstallDependencies(); }
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
		if (ImGui.Button("Close", new Vector2(100, 0))) { CheckDependencies(); }
	}

	private void RenderFailed()
	{
		ImGui.Text("Installing dependencies: ");
		ImGui.SameLine();
		RenderDownloadProgress();
		ImGui.SameLine();
		if (ImGui.Button("Retry", new Vector2(100, 0))) { CheckDependencies(); }
	}

	private void RenderDownloadProgress()
	{
		Vector2 progressSize = new(875, 0);

		foreach (KeyValuePair<string, float> progress in _installProgress)
		{
			if (progress.Value == _depExtracting)
			{
				ImGui.PushStyleColor(ImGuiCol.PlotHistogram, _colorProgress);
				ImGui.ProgressBar(1, progressSize, "Extracting");
				ImGui.PopStyleColor();
			}
			else if (progress.Value == _depComplete)
			{
				ImGui.PushStyleColor(ImGuiCol.PlotHistogram, _colorDone);
				ImGui.ProgressBar(1, progressSize, "Complete");
				ImGui.PopStyleColor();
			}
			else if (progress.Value == _depFailed)
			{
				ImGui.PushStyleColor(ImGuiCol.PlotHistogram, _colorError);
				ImGui.ProgressBar(1, progressSize, "Error");
				ImGui.PopStyleColor();
			}
			else
			{
				ImGui.PushStyleColor(ImGuiCol.PlotHistogram, _colorProgress);
				ImGui.ProgressBar(progress.Value / 100, progressSize);
				ImGui.PopStyleColor();
			}
		}
	}

	private enum ViewMode
	{
		Confirm,
		Installing,
		Complete,
		Failed,
		Hidden
	}
}