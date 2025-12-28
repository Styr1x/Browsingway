using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Browsingway.Services;

internal sealed record Dependency(string Url, string Directory, string Version, string Checksum);

public sealed class DependencyManager : IDisposable
{
	private const string DownloadDir = "downloads";

	// Per-dependency special-cased progress values
	public const short DepExtracting = -1;
	public const short DepComplete = -2;
	public const short DepFailed = -3;

	private static readonly Dependency[] Dependencies =
	[
		new("https://github.com/Styr1x/Browsingway/releases/download/cef-binaries/cefsharp-{VERSION}.zip", "cef",
			"143.0.9+ge88e818+chromium-143.0.7499.40",
			"2911B142BE2B9F8555FD7A2FF28B47ADFFF531D4E509BD8E0020FA0771DADAA4")
	];

	private readonly string _debugCheckDir;
	private readonly string _dependencyDir;
	private readonly ConcurrentDictionary<string, float> _installProgress = new();
	private readonly IServiceContainer _services;
	private readonly HttpClient _httpClient = new();

	private ViewMode _viewMode = ViewMode.Hidden;

	public event EventHandler? StateChanged;

	internal IReadOnlyList<Dependency>? MissingDependencies { get; private set; }
	public IReadOnlyDictionary<string, float> InstallProgress => _installProgress;
	public ViewMode State => _viewMode;

	public DependencyManager(IServiceContainer services, string pluginDir, string pluginConfigDir)
	{
		_services = services;
		_dependencyDir = Path.Join(pluginConfigDir, "dependencies");
		_debugCheckDir = Path.GetDirectoryName(pluginDir) ?? pluginDir;
	}

	public void Dispose()
	{
		_httpClient.Dispose();
	}

	public event EventHandler? DependenciesReady;

	public void Initialise()
	{
		CheckDependencies();
	}

	public void CheckDependencies()
	{
		var missing = Dependencies.Where(DependencyMissing).ToArray();
		MissingDependencies = missing;
		
		if (missing.Length == 0)
		{
			SetState(ViewMode.Hidden);
			DependenciesReady?.Invoke(this, EventArgs.Empty);
		}
		else
		{
			SetState(ViewMode.Confirm);
		}
	}

	private void SetState(ViewMode state)
	{
		if (_viewMode != state)
		{
			_viewMode = state;
			StateChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	private static bool DependencyMissing(Dependency dependency)
	{
		string versionFilePath = Path.Combine(GetDependencyPathStatic(dependency), "VERSION");

		try
		{
			string versionContents = File.ReadAllText(versionFilePath);
			return !versionContents.Contains(dependency.Version);
		}
		catch
		{
			return true;
		}
	}

	public void InstallDependencies()
	{
		if (MissingDependencies is null)
		{
			return;
		}

		SetState(ViewMode.Installing);
		_services.PluginLog.Info("Installing dependencies...");

		IEnumerable<Task> installTasks = MissingDependencies.Select(InstallDependencyAsync);
		Task.WhenAll(installTasks).ContinueWith(_ =>
		{
			bool failed = _installProgress.Any(pair => pair.Value == DepFailed);
			SetState(failed ? ViewMode.Failed : ViewMode.Complete);
			_services.PluginLog.Info($"Dependency install {_viewMode}.");

			try { Directory.Delete(Path.Combine(_dependencyDir, DownloadDir), true); }
			catch { /* Ignore cleanup errors */ }
		});
	}

	private async Task InstallDependencyAsync(Dependency dependency)
	{
		_services.PluginLog.Info($"Downloading {dependency.Directory} {dependency.Version}");

		// Ensure the downloads dir exists
		string downloadDir = Path.Combine(_dependencyDir, DownloadDir);
		Directory.CreateDirectory(downloadDir);

		// Get the file name we'll download to - if it's already in downloads, it may be corrupt, delete
		string filePath = Path.Combine(downloadDir, $"{dependency.Directory}-{dependency.Version}.zip");
		File.Delete(filePath);

		try
		{
			// Download with progress reporting
			string url = dependency.Url.Replace("{VERSION}", dependency.Version);
			using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
			response.EnsureSuccessStatusCode();

			long? totalBytes = response.Content.Headers.ContentLength;
			await using var contentStream = await response.Content.ReadAsStreamAsync();
			await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

			var buffer = new byte[8192];
			long totalRead = 0;
			int bytesRead;

			while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
			{
				await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
				totalRead += bytesRead;

				if (totalBytes > 0)
				{
					float percentage = (float)totalRead / totalBytes.Value * 100;
					_installProgress.AddOrUpdate(dependency.Directory, percentage, (_, oldValue) => Math.Max(oldValue, percentage));
				}
			}
		}
		catch (Exception ex)
		{
			_services.PluginLog.Error(ex, $"Failed to download {dependency.Directory}");
			_installProgress.AddOrUpdate(dependency.Directory, DepFailed, (_, _) => DepFailed);
			return;
		}

		// Download complete, mark as extracting
		_installProgress.AddOrUpdate(dependency.Directory, DepExtracting, (_, _) => DepExtracting);

		// Calculate the checksum for the download
		string downloadedChecksum;
		try
		{
			await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
			byte[] rawHash = await SHA256.HashDataAsync(stream);
			downloadedChecksum = Convert.ToHexString(rawHash);
		}
		catch (Exception ex)
		{
			_services.PluginLog.Error(ex, $"Failed to calculate checksum for {filePath}");
			downloadedChecksum = "FAILED";
		}

		// Make sure the checksum matches
		if (!string.Equals(downloadedChecksum, dependency.Checksum, StringComparison.OrdinalIgnoreCase))
		{
			_services.PluginLog.Error(
				$"Mismatched checksum for {filePath}: Got {downloadedChecksum} but expected {dependency.Checksum}");
			_installProgress.AddOrUpdate(dependency.Directory, DepFailed, (_, _) => DepFailed);
			File.Delete(filePath);
			return;
		}

		_installProgress.AddOrUpdate(dependency.Directory, DepComplete, (_, _) => DepComplete);

		// Extract to the destination dir
		string destinationDir = GetDependencyPath(dependency);
		try { Directory.Delete(destinationDir, true); }
		catch { /* Ignore if doesn't exist */ }

		ZipFile.ExtractToDirectory(filePath, destinationDir);

		// Clear out the downloaded file now we're done with it
		File.Delete(filePath);
	}

	public string GetDependencyPathFor(string dependencyDir)
	{
		Dependency dependency = Dependencies.First(d => d.Directory == dependencyDir)
			?? throw new Exception($"Unknown dependency {dependencyDir}");

		return GetDependencyPath(dependency);
	}

	private string GetDependencyPath(Dependency dependency)
	{
		string localDebug = Path.Combine(_debugCheckDir, dependency.Directory);
		return Directory.Exists(localDebug) ? localDebug : Path.Combine(_dependencyDir, dependency.Directory);
	}

	private static string GetDependencyPathStatic(Dependency dependency)
	{
		// For static context (DependencyMissing), we can't check debug path
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			"XIVLauncher", "pluginConfigs", "Browsingway", "dependencies", dependency.Directory);
	}

	public enum ViewMode
	{
		Confirm,
		Installing,
		Complete,
		Failed,
		Hidden
	}
}
