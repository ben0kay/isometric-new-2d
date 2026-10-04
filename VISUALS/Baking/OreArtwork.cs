// Shares cached ore artwork using the existing disk-backed baker.
// Waits until initial scene setup finishes before attaching a temporary viewport.
using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public static class OreArtwork
{
	#region Cache
	private static readonly Dictionary<string, Task<ImageTexture>> Tasks = new();
	#endregion

	#region Requests
	// =========================================================
	// Share one artwork task between deposits using the same drawing.
	public static Task<ImageTexture> Get(Node host, OreDefinition definition)
	{
		if (definition == null || definition.FallbackDrawing == null)
			throw new InvalidOperationException(
				"Ore artwork requires a definition with a fallback drawing scene.");

		string key = $"{definition.Id}|{definition.ArtworkRevision}|" +
			$"{definition.FallbackDrawing.ResourcePath}|{definition.BakeSize}";

		if (Tasks.TryGetValue(key, out Task<ImageTexture> task) &&
			!task.IsFaulted && !task.IsCanceled)
			return task;

		Node stableHost = host.GetTree().Root;
		task = BakeAsync(stableHost, definition, key);
		Tasks[key] = task;
		return task;
	}
	#endregion

	#region Baking
	// =========================================================
	// Let scene construction finish before the baker adds its viewport.
	private static async Task<ImageTexture> BakeAsync(
		Node host, OreDefinition definition, string key)
	{
		await host.ToSignal(
			host.GetTree(), SceneTree.SignalName.ProcessFrame);

		if (!GodotObject.IsInstanceValid(host) ||
			!host.IsInsideTree() || host.IsQueuedForDeletion())
			throw new InvalidOperationException(
				"OreBake: artwork host was removed before baking.");

		return await ArtworkBaker.LoadOrBake(
			host, "OreBake", key, definition.BakeSize,
			() => definition.FallbackDrawing.Instantiate<Node2D>());
	}
	#endregion
}
