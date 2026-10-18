namespace GymApp.Services;

/// <summary>
/// Which form field a validation message belongs to, so the UI can show it next to that field (the
/// <c>Field</c> on a result record). Null on a result means the message is about the form as a whole.
/// </summary>
public static class FormField
{
    public const string Username = nameof(Username);
    public const string Password = nameof(Password);
    public const string Name = nameof(Name);
    public const string Address = nameof(Address);
    public const string Days = nameof(Days);
    public const string MuscleGroup = nameof(MuscleGroup);
    public const string Sex = nameof(Sex);
    public const string BirthDate = nameof(BirthDate);
    public const string Height = nameof(Height);
    public const string ActivityLevel = nameof(ActivityLevel);
    public const string Units = nameof(Units);
    public const string Weight = nameof(Weight);
    public const string Reps = nameof(Reps);
    public const string OneRepMax = nameof(OneRepMax);
}
