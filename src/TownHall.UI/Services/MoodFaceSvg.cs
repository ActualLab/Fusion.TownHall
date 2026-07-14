using System.Globalization;

namespace TownHall.UI.Services;

/// <summary>
/// Renders the room mood as an SVG face (crude v1): fill goes gloomy-blue to sunny-yellow
/// and the mouth goes frown to smile as the average mood moves from 1 to 5.
/// </summary>
public static class MoodFaceSvg
{
    public static string Render(double? avg)
    {
        var fill = avg is { } a ? LerpColor(0x8fa8c9, 0xffd54f, (a - 1) / 4) : "#cfcfcf";
        var mouthCy = avg is { } a2 ? 132 + (a2 - 3) * 19 : 132;
        var cy = mouthCy.ToString("0.#", CultureInfo.InvariantCulture);
        return $"""
            <svg viewBox="0 0 200 200" xmlns="http://www.w3.org/2000/svg">
              <circle cx="100" cy="100" r="80" fill="{fill}" />
              <circle cx="72" cy="82" r="7" fill="#333" />
              <circle cx="128" cy="82" r="7" fill="#333" />
              <path d="M 62 132 Q 100 {cy} 138 132" stroke="#333" stroke-width="6"
                    stroke-linecap="round" fill="none" />
            </svg>
            """;
    }

    // Private methods

    private static string LerpColor(int from, int to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        var r = Lerp(from >> 16 & 0xff, to >> 16 & 0xff);
        var g = Lerp(from >> 8 & 0xff, to >> 8 & 0xff);
        var b = Lerp(from & 0xff, to & 0xff);
        return $"#{r:x2}{g:x2}{b:x2}";

        int Lerp(int c1, int c2) => (int)Math.Round(c1 + (c2 - c1) * t);
    }
}
