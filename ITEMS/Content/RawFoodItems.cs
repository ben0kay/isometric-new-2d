// Defines raw foods, their consumption settings and primitive inventory artwork.
// Consumption timing and survival changes remain handled by shared systems.
using Godot;
using System.Collections.Generic;

public static class RawFoodItems
{
	#region Definitions
	// =========================================================
	// Register raw food definitions with their shared consumption resources.
	public static void Register(List<ItemDefinition> items)
	{
		ItemDefinition berry = ItemArtwork.Create(
			"alien_berry", "Alien Berry", "BERRY",
			40, 0.02f, 0.025f, new Color(0.65f, 0.35f, 0.85f),
			BerryDrawing());

		berry.Consumable = new ConsumableDefinition
		{
			FoodRestored = 5f,
			WaterRestored = 3f,
			FatigueRelief = 0f,
			UseIntervalSeconds = 0.75
		};

		items.Add(berry);
	}
	#endregion

	#region Artwork
	// =========================================================
	// Draw the alien berry cluster inside the shared icon canvas.
	private static string BerryDrawing()
	{
		return
			"<ellipse cx='25' cy='39' rx='16' ry='4' fill='#14212b' opacity='.35'/>" +
			"<path d='M24 24 Q19 12 29 5' fill='none' stroke='#729c74' stroke-width='3'/>" +
			"<path d='M26 13 Q35 4 41 12 Q33 19 26 13Z' fill='#72b7a0'/>" +
			"<circle cx='17' cy='27' r='10' fill='#54317c' stroke='#30234e' stroke-width='2'/>" +
			"<circle cx='30' cy='27' r='10' fill='#754596' stroke='#30234e' stroke-width='2'/>" +
			"<circle cx='24' cy='35' r='9' fill='#9051b0' stroke='#30234e' stroke-width='2'/>" +
			"<ellipse cx='14' cy='23' rx='3' ry='2' fill='#b995d9'/>" +
			"<ellipse cx='27' cy='23' rx='3' ry='2' fill='#d3b1ec'/>" +
			"<ellipse cx='21' cy='32' rx='3' ry='2' fill='#d3b1ec'/>";
	}
	#endregion
}
