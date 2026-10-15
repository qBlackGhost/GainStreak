namespace GymApp.Tests;

/// <summary>Reads the saved API responses in the Fixtures folder, so no test calls a real API.</summary>
public static class Fixtures
{
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
