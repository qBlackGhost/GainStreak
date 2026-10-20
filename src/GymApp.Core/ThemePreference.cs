namespace GymApp.Core;

/// <summary>The look the user picked in Profile → Appearance. System follows the Windows or macOS setting.
/// Stored as its name in the database, so don't rename the members.</summary>
public enum ThemePreference { System, Light, Dark }
