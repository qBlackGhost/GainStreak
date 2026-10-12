namespace GymApp.Core;

public static class OneRepMax
{
    /// <summary>Epley estimate. A single rep is the weight itself.</summary>
    public static double Epley(double weightKg, int reps) =>
        reps <= 1 ? weightKg : weightKg * (1 + reps / 30.0);
}