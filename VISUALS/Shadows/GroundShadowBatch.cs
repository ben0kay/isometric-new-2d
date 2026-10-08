// Draws cached shadow commands for one shared area.
// Static commands remain cached until their batch is explicitly changed.
using Godot;
using System.Collections.Generic;

public partial class GroundShadowBatch : Node2D
{
    #region Configuration

    public bool Contact { get; set; }
    public Texture2D ContactTexture { get; set; }
    public List<GroundShadow.Entry> Entries { get; set; } = new();

    #endregion

    #region Drawing

    // =========================================================
    // Render shared contact patches or projected artwork alpha.
    public override void _Draw()
    {
        Transform2D inverse = GlobalTransform.AffineInverse();

        foreach (GroundShadow.Entry entry in Entries)
        {
            if (!entry.Alive || !entry.Ready || !entry.Visible)
                continue;

            if (Contact)
            {
                if (!entry.Contact) continue;

                DrawSetTransformMatrix(inverse * entry.ContactTransform);
                DrawTextureRect(
                    ContactTexture,
                    new Rect2(-32f, -16f, 64f, 32f),
                    false, entry.ContactColour);
            }
            else
            {
                if (!entry.Cast || entry.Texture == null) continue;

                DrawSetTransformMatrix(inverse * entry.CastTransform);
                DrawTextureRectRegion(
                    entry.Texture, entry.Rectangle,
                    entry.Region, entry.CastColour);
            }
        }

        DrawSetTransformMatrix(Transform2D.Identity);
    }

    #endregion
}