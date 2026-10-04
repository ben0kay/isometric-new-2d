// Defines eight irregular rock silhouettes with broad fractured stone faces.
// Polygon clipping runs during baking; gameplay reuses the finished atlas sprites.
using Godot;

public static class RockDrawing
{
    #region Configuration
    public const int VariantCount = 8;

    // Different silhouettes: boulder, low rock, pillar, slab,
    // leaning rock, broken crest, angular boulder and eroded block.
    private static readonly Vector2[][] Outlines =
    {
        new Vector2[]
        {
            new(-58, -6), new(-59, -45), new(-43, -73),
            new(-12, -88), new(35, -78), new(58, -43),
            new(55, 8), new(22, 24), new(-34, 19)
        },
        new Vector2[]
        {
            new(-61, -4), new(-56, -33), new(-31, -55),
            new(12, -60), new(48, -41), new(62, -10),
            new(51, 12), new(6, 23), new(-39, 16)
        },
        new Vector2[]
        {
            new(-43, 3), new(-49, -36), new(-38, -86),
            new(-12, -105), new(24, -96), new(42, -61),
            new(47, -13), new(28, 20), new(-14, 24)
        },
        new Vector2[]
        {
            new(-62, 1), new(-59, -31), new(-43, -61),
            new(-10, -68), new(45, -62), new(61, -42),
            new(58, 7), new(28, 22), new(-40, 19)
        },
        new Vector2[]
        {
            new(-55, 5), new(-61, -29), new(-44, -68),
            new(3, -89), new(38, -74), new(60, -36),
            new(48, 14), new(8, 24), new(-36, 18)
        },
        new Vector2[]
        {
            new(-60, 0), new(-57, -42), new(-30, -68),
            new(-9, -61), new(8, -86), new(39, -74),
            new(57, -44), new(60, 2), new(33, 20), new(-31, 22)
        },
        new Vector2[]
        {
            new(-59, -3), new(-48, -50), new(-17, -92),
            new(20, -88), new(45, -53), new(60, -17),
            new(47, 13), new(15, 24), new(-39, 17)
        },
        new Vector2[]
        {
            new(-60, -6), new(-55, -49), new(-29, -77),
            new(16, -81), new(42, -68), new(36, -43),
            new(56, -28), new(60, 4), new(27, 22), new(-34, 19)
        }
    };

    // Independent face boundaries prevent a shared central convergence.
    private static readonly float[] RidgeTop = { -10, 12, -8, -18, 15, -4, 4, -14 };
    private static readonly float[] RidgeMiddle = { 2, -8, 8, 14, -12, 11, -7, 6 };
    private static readonly float[] RidgeBottom = { -6, 4, 0, -10, 6, -12, 12, -3 };
    private static readonly float[] CapHeight = { -57, -38, -76, -46, -60, -58, -67, -54 };
    #endregion

    #region Drawing
    // =========================================================
    // Bake a distinct rock silhouette with broad faces and chipped edges.
    public static void Draw(CanvasItem canvas, WorldAtmosphere atmosphere, int variant)
    {
        variant = Mathf.Clamp(variant, 0, VariantCount - 1);
        Vector2[] outline = Outlines[variant];
        float top = RidgeTop[variant], middle = RidgeMiddle[variant];
        float bottom = RidgeBottom[variant], cap = CapHeight[variant];

        Color stone = (variant % 3) switch
        {
            0 => new Color("#444a49"),
            1 => new Color("#4a4740"),
            _ => new Color("#41494e")
        };

        // The underlying shaded side fills the entire silhouette.
        canvas.DrawColoredPolygon(outline,
            Shade(stone, atmosphere, new Vector2(1f, 0.15f), 0.95f));

        // Broad illuminated face with an irregular vertical fracture.
        DrawFace(canvas, outline, new Vector2[]
        {
            new(-90, -140), new(top, -140),
            new(middle, -35), new(bottom, 50), new(-90, 50)
        }, Shade(stone, atmosphere, new Vector2(-1f, -0.2f), 1f));

        // An uneven upper ledge gives the rock a solid top surface.
        DrawFace(canvas, outline, new Vector2[]
        {
            new(-90, -140), new(90, -140),
            new(90, cap + 6), new(top + 15, cap), new(-90, cap + 12)
        }, Shade(stone, atmosphere, new Vector2(-0.35f, -1f), 1.04f));

        // A narrow chipped edge breaks up selected broad faces.
        if (variant is 0 or 2 or 4 or 7)
        {
            DrawFace(canvas, outline, new Vector2[]
            {
                new(-90, -140), new(-42, -140),
                new(-45, -49), new(-55, -12), new(-90, 12)
            }, Shade(stone, atmosphere, new Vector2(-0.7f, -0.45f), 0.84f));
        }

        // Separate broken corners vary the side profile and shading.
        if (variant is 1 or 3 or 5 or 7)
        {
            DrawFace(canvas, outline, new Vector2[]
            {
                new(32, -38), new(85, -16), new(85, 60),
                new(9, 60), new(26, 8)
            }, Shade(stone, atmosphere, new Vector2(0.65f, -0.1f), 1.12f));
        }
        else
        {
            DrawFace(canvas, outline, new Vector2[]
            {
                new(43, -67), new(90, -49), new(90, 40),
                new(36, 40), new(47, -8)
            }, Shade(stone, atmosphere, new Vector2(1f, 0.35f), 0.78f));
        }

        // An irregular lower face anchors the rock into the ground.
        DrawFace(canvas, outline, new Vector2[]
        {
            new(-90, 9), new(-12, -7), new(28, 5),
            new(90, -7), new(90, 60), new(-90, 60)
        }, Shade(stone, atmosphere, new Vector2(-0.15f, 1f), 1.05f));
    }
    #endregion

    #region Face Helpers
    // =========================================================
    // Clip a broad stone face to the outline before drawing its visible pieces.
    private static void DrawFace(
        CanvasItem canvas, Vector2[] outline, Vector2[] face, Color color)
    {
        foreach (Vector2[] polygon in Geometry2D.IntersectPolygons(outline, face))
        {
            if (polygon.Length >= 3)
                canvas.DrawColoredPolygon(polygon, color);
        }
    }

    // =========================================================
    // Shade a face using the existing fixed sunlight and a small tonal adjustment.
    private static Color Shade(
        Color stone, WorldAtmosphere atmosphere, Vector2 normal, float multiplier)
    {
        Color color = new(stone.R * multiplier, stone.G * multiplier,
            stone.B * multiplier, 1f);
        return atmosphere != null ? atmosphere.ShadeFace(color, normal) : color;
    }
    #endregion
}