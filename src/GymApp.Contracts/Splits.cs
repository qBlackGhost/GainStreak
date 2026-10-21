namespace GymApp.Contracts;

/// <summary>The split the user trains with, sent with the stats upload. <see cref="PresetId"/> is the built-in preset's
/// id (the same on every phone); null means one of the user's own custom splits, whose ids are local.</summary>
public sealed record ActiveSplitRequest(int? PresetId);

/// <summary>How many users train with one preset.</summary>
public sealed record SplitUsageRow(int PresetId, int Users);

/// <summary>Everyone with a split chosen: <see cref="Users"/> in all, split into <see cref="Presets"/> (only presets
/// somebody uses) and <see cref="Custom"/>.</summary>
public sealed record SplitUsageResponse(int Users, IReadOnlyList<SplitUsageRow> Presets, int Custom);
