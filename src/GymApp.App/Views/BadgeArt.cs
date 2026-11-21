using System.Collections.Generic;

namespace GymApp.App.Views;

/// <summary>The badge drawing's data (Phase 25): each look's colours and each glyph's path on its 24-unit grid, as
/// plain strings.</summary>
public static class BadgeArt
{
    public sealed record Material(string[] Stops, string Core, string Line, string? Glow, string? GlyphColor = null);

    public static readonly Dictionary<BadgeLook, Material> Materials = new()
    {
        [BadgeLook.Steel] = new(["#4A4A4A", "#D6D6D6", "#6E6E6E"], "#0B0B0B", "#14FFFFFF", null),
        [BadgeLook.Lime] = new(["#8DB51E", "#C6F432", "#E2FF8A"], "#0B0B0B", "#2EC6F432", "#38C6F432"),
        [BadgeLook.Fire] = new(["#C81D00", "#FF4A00", "#FFB000"], "#0B0B0B", "#38FF7A00", "#59FF4A00"),
        [BadgeLook.Blaze] = new(["#FF7A00", "#FFD400", "#FFF6B0"], "#120C02", "#47FFD400", "#73FFD400"),
        [BadgeLook.Ice] = new(["#1F6FD1", "#5CB8FF", "#D4F0FF"], "#06101A", "#405CB8FF", "#4D5CB8FF"),
        [BadgeLook.Limited] = new(["#C6F432", "#FFD400", "#FF4A00"], "#0B0B0B", "#38FFD400", "#4DFF9A00"),
        [BadgeLook.Locked] = new(["#2A2A2A", "#3A3A3A", "#2A2A2A"], "#0B0B0B", "#0AFFFFFF", null, "#5A5A5A"),
        [BadgeLook.Hidden] = new(["#161616", "#222222", "#161616"], "#080808", "#4A4A4A", null, "#9A9A9A"),
        [BadgeLook.Secret] = new(["#5A5A5A", "#8A8A8A", "#5A5A5A"], "#0B0B0B", "#9A9A9A", null, "#E6E6E6"),
    };

    // The glyphs on their 24-unit grid, as in the spec's appendix
    public static readonly Dictionary<string, string> GlyphPaths = new()
    {
        ["flame"] = "M12 22c4 0 7-3 7-7 0-5-4-7-5-12-2 3-3 5-3 7-1-1-2-2-2-4-2 2-4 5-4 9 0 4 3 7 7 7z",
        ["week"] = "M5 4h14a2 2 0 0 1 2 2v13a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2zM3 9h18M8 2v4M16 2v4M9 15l2 2 4-4",
        ["snow"] = "M12 2v20M3.4 7l17.2 10M3.4 17L20.6 7M9.5 3.6L12 5.6l2.5-2M9.5 20.4L12 18.4l2.5 2M3.6 10.4l3.1-.4-.9-3M20.4 13.6l-3.1.4.9 3M3.6 13.6l3.1.4-.9 3M20.4 10.4l-3.1-.4.9-3",
        ["comeback"] = "M4 12a8 8 0 1 0 3-6.2M4 4v4h4",
        ["bolt"] = "M13 2L4 14h7l-1 8 9-12h-7z",
        ["fuel"] = "M12 7.5c-1-1.8-3-2.8-5-2-3 1.2-4 5-3 9 1 4 4 6.5 6 5.5 1-.4 1.3-.5 2-.5s1 .1 2 .5c2 1 5-1.5 6-5.5 1-4 0-7.8-3-9-2-.8-4 .2-5 2zM12 7.5c0-2 1-4 3-5",
        ["scale"] = "M5 4h14a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2zM8 11a4 4 0 0 1 8 0M12 11l1.6-2.2",
        ["pin"] = "M12 22s7-6.5 7-12a7 7 0 1 0-14 0c0 5.5 7 12 7 12zM9.5 10a2.5 2.5 0 1 0 5 0a2.5 2.5 0 1 0-5 0",
        ["map"] = "M9 4L3 6v14l6-2 6 2 6-2V4l-6 2-6-2zM9 4v14M15 6v14",
        ["friends"] = "M5.5 8a3.5 3.5 0 1 0 7 0a3.5 3.5 0 1 0-7 0M2.5 20c0-3.6 2.9-6 6.5-6s6.5 2.4 6.5 6M14.5 9a2.5 2.5 0 1 0 5 0a2.5 2.5 0 1 0-5 0M16.5 14.2c2.9.3 5 2.4 5 5.8",
        ["star"] = "M12 3l2.7 5.6 6.1.9-4.4 4.3 1 6.1L12 17l-5.4 2.9 1-6.1-4.4-4.3 6.1-.9z",
        ["car"] = "M3 17v-5l3-1 3-4h6l4 4h2v6M5 17a2 2 0 1 0 4 0a2 2 0 1 0-4 0M15 17a2 2 0 1 0 4 0a2 2 0 1 0-4 0M9 17h6",
        ["elephant"] = "M21 15v-5a4 4 0 0 0-4-4h-7a5.5 5.5 0 0 0-5.5 5.5V19H6M9 14v5h2.5v-3h5v3H19v-4M21 11l1.5 2.5M9 7.5c-2 1-2 4 1 5M7 10.5h.01",
        ["whale"] = "M2 12c0 4 4 7 10 7 5 0 8-2 9-5l1-4-3 1c-1-3-4-5-8-5-5 0-9 2-9 6zM7 11h.01M11 6c0-1.5 1-2.5 2-3M11 6c0-1.5-1-2.5-2-3",
        ["station"] = "M2 6h5v12H2zM17 6h5v12h-5zM7 12h10M10 9h4v6h-4zM2 10h5M2 14h5M17 10h5M17 14h5",
        ["moon"] = "M20 14.5A8 8 0 1 1 9.5 4a6.5 6.5 0 0 0 10.5 10.5z",
        ["sunrise"] = "M4 18h16M7 18a5 5 0 0 1 10 0M12 6v3M5.6 10.6l1.6 1.6M18.4 10.6l-1.6 1.6M2 21h20",
        ["double"] = "M4 6v6M10 6v6M4 9h6M14 12v6M20 12v6M14 15h6",
        ["clock"] = "M3 12a9 9 0 1 0 18 0a9 9 0 1 0-18 0M12 7v5l3 2",
        ["plate"] = "M3 12a9 9 0 1 0 18 0a9 9 0 1 0-18 0M9 12a3 3 0 1 0 6 0a3 3 0 1 0-6 0",
        ["sparkle"] = "M12 3v4M12 17v4M3 12h4M17 12h4M5.6 5.6l2.8 2.8M15.6 15.6l2.8 2.8M5.6 18.4l2.8-2.8M15.6 8.4l2.8-2.8",
        ["month1"] = "M5 4h14a2 2 0 0 1 2 2v13a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2zM3 9h18M8 2v4M16 2v4M10.5 13.5l2-1.5v6",
        ["weekend"] = "M5 4h14a2 2 0 0 1 2 2v13a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2zM3 9h18M8 2v4M16 2v4M14 13h4v4h-4z",
        ["grid"] = "M3 3h7v7H3zM14 3h7v7h-7zM3 14h7v7H3zM14 14h7v7h-7z",
        ["bodyweight"] = "M3 4h18M6 2v4M18 2v4M10 9a2 2 0 1 0 4 0a2 2 0 1 0-4 0M8 4l3 4M16 4l-3 4M12 11v5M9 22l3-6 3 6",
        ["reps"] = "M17 2l3 3-3 3M4 11V9a4 4 0 0 1 4-4h12M7 22l-3-3 3-3M20 13v2a4 4 0 0 1-4 4H4",
        ["compass"] = "M3 12a9 9 0 1 0 18 0a9 9 0 1 0-18 0M15.5 8.5l-2 5-5 2 2-5z",
        ["cardio"] = "M20.8 5.6a5 5 0 0 0-7.1 0L12 7.3l-1.7-1.7a5 5 0 0 0-7.1 7.1L12 21.5l8.8-8.8a5 5 0 0 0 0-7.1zM3 12h4l2-3 3 6 2-3h7",
        ["recruiter"] = "M4 21c0-4 3-7 7-7M7 8a4 4 0 1 0 8 0a4 4 0 1 0-8 0M18 14v6M15 17h6",
        ["trophy"] = "M8 21h8M12 17v4M7 4h10v5a5 5 0 0 1-10 0V4zM7 6H4a3 3 0 0 0 3 4M17 6h3a3 3 0 0 1-3 4",
        ["cake"] = "M4 21h16M5 21v-8h14v8M5 16c2 1.5 4.5 1.5 7 0s5-1.5 7 0M12 13V9M12 6.5c-1-1-1-2 0-3.5 1 1.5 1 2.5 0 3.5z",
        ["tree"] = "M12 2l5 7h-3l5 7H5l5-7H7zM12 16v5",
        ["checklist"] = "M9 6h11M9 12h11M9 18h11M4 6l1 1 2-2M4 12l1 1 2-2M4 18l1 1 2-2",
        ["mug"] = "M4 9h12v6a5 5 0 0 1-5 5H9a5 5 0 0 1-5-5zM16 11h2a2 2 0 0 1 0 4h-2M8 2c-1 1.5 1 2.5 0 4M12 2c-1 1.5 1 2.5 0 4",
        ["grass"] = "M2 21h20M5 21c0-4-1-7-3-9M9 21c0-5 1-9 4-12M14 21c0-3 1-6 4-8M19 21c0-2 0-4 2-6",
        ["leg"] = "M7.5 3.5C11 3.6 14.5 5.5 16.5 9C17.3 10.3 17 11.3 16.2 12.2C15.6 14.5 15.2 16.5 15.2 18.6C16.8 19.4 18.6 19.6 19.6 20.4C19.8 21 19.4 21.4 18.8 21.4H12.6C12 21.4 11.8 20.8 12 20.2C12.2 19 11.6 17.8 10.9 16C10.2 14.2 10.6 12.6 11.8 11.6C10 10.6 8 9.6 6.6 8.2C5.6 7.2 5.8 3.6 7.5 3.5Z",
        ["flag"] = "M5 21V4h13l-3 4.5 3 4.5H5",
    };

}
