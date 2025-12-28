using Dalamud.Plugin.Services;
using System.Runtime.CompilerServices;

namespace Browsingway.Extensions;

/// <summary>
/// Extension methods for Task to handle fire-and-forget async operations safely.
/// </summary>
internal static class TaskExtensions
{
	/// <summary>
	/// Safely executes an async operation without awaiting, with error logging.
	/// Use this instead of discarding tasks with _ = SomeAsyncMethod().
	/// </summary>
	/// <param name="task">The task to execute.</param>
	/// <param name="log">Optional logger for error reporting.</param>
	/// <param name="caller">Auto-populated caller member name for error context.</param>
	public static async void FireAndForget(
		this Task? task,
		IPluginLog? log = null,
		[CallerMemberName] string caller = "")
	{
		if (task is null) return;

		try
		{
			await task.ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			log?.Error(ex, $"Fire-and-forget error in {caller}");
		}
	}

	/// <summary>
	/// Safely executes an async operation without awaiting, with error logging.
	/// Generic version for Task{T}.
	/// </summary>
	public static async void FireAndForget<T>(
		this Task<T>? task,
		IPluginLog? log = null,
		[CallerMemberName] string caller = "")
	{
		if (task is null) return;

		try
		{
			await task.ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			log?.Error(ex, $"Fire-and-forget error in {caller}");
		}
	}
}
