namespace GymApp.Server.Data;

public enum EmailCodePurpose { ConfirmEmail, ResetPassword }

/// <summary>A code emailed to a user, one per user and purpose: a new one replaces the old. Only a hash is stored, and
/// it's deleted once used. Deleted with the user.</summary>
public class EmailCode
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    /// <summary>How long before another code can be asked for, so a form can't be used to fill someone's inbox.</summary>
    public static readonly TimeSpan ResendAfter = TimeSpan.FromMinutes(1);

    public long Id { get; set; }
    public int UserId { get; set; }
    public EmailCodePurpose Purpose { get; set; }
    public string CodeHash { get; set; } = "";
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
