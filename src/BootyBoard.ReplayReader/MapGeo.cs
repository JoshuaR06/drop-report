using System.Text.Json;

namespace BootyBoard;

/// <summary>
/// Where the game's map image sits in the world. From the game files: the main map's capture camera
/// (FortMapUIData -> CaptureCameraFrontendData: OrthoWidth + WorldOffset). The image is square and covers
/// Width world units centred on (CenterX, CenterY); world X runs left to right and world Y top to bottom, as in game.
/// Verified 11-16 on game version 43.10: the camera's own numbers were the best fit of 450 tested placements
/// (93% of player positions on land).
/// </summary>
public sealed record MapGeo(string Build, int Season, string MapName, double CenterX, double CenterY, double Width, List<MapGeo.Poi> Pois)
{
    public sealed record Poi(string Name, double X, double Y);

    /// <summary>World position -> 0..1 image coordinates (u right, v down).</summary>
    public (double u, double v) ToImage(double x, double y) => ((x - CenterX) / Width + 0.5, (y - CenterY) / Width + 0.5);

    public static MapGeo Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<MapGeo>(File.ReadAllText(path), Json.Options) : null; }
        catch { return null; }
    }
}
