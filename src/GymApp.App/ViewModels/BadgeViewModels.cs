using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymApp.App.Views;
using GymApp.Core;
using GymApp.Services;
using GymApp.Social;

namespace GymApp.App.ViewModels;

/// <summary>One badge as the screens show it (Phase 25). A hidden badge the user hasn't earned is masked: "Hidden",
/// the dashed "?", and its hint instead of its rule.</summary>
public sealed class BadgeItemViewModel
{
    /// <param name="masked">Draw it as the dashed "?" even if earned: a friend's hidden badge the viewer hasn't earned.</param>
    public BadgeItemViewModel(BadgeDefinition badge, BadgeProgress progress, bool imperial, bool? masked = null)
    {
        Badge = badge;
        Progress = progress;
        Imperial = imperial;
        IsMasked = masked ?? (badge.Hidden && !progress.Earned);
    }

    public BadgeDefinition Badge { get; }
    public BadgeProgress Progress { get; }
    public bool Imperial { get; }
    public bool IsMasked { get; }

    public string Id => Badge.Id;
    public bool IsEarned => Progress.Earned;
    public int Tier => Progress.Tier;
    public string Name => IsMasked ? "Hidden" : Badge.Name;
    public string Glyph => BadgeCatalog.GlyphFor(Badge, Math.Max(Tier, 1)) ?? "";
    public string? Text => Badge.Text;
    public BadgeLook Look => BadgeLooks.For(Badge, IsEarned, Tier, IsMasked);

    /// <summary>The rule, or the hint while it's hidden.</summary>
    public string Rule => IsMasked ? Badge.Hint ?? "" : Badge.Rule;

    /// <summary>"Fire · 100 days", "Elephant", "Earned", "Locked", or "Hidden".</summary>
    public string Status => IsMasked ? "Hidden" : BadgeDisplay.Status(Badge, IsEarned, Tier, Imperial);

    /// <summary>Short, for the grid's tiles: "FIRE", "ELEPHANT", "EARNED", "9 / 14 days".</summary>
    public string Short => IsMasked ? "?"
        : IsEarned ? (Badge.IsTiered ? BadgeCatalog.TierName(Badge, Tier)!.ToUpperInvariant() : "EARNED")
        : ProgressText ?? "LOCKED";

    public string? ProgressText => IsMasked ? null : BadgeDisplay.Progress(Badge, Progress, Imperial);
    public double Ratio => BadgeDisplay.Ratio(Progress);
    public bool ShowsProgress => ProgressText is not null && !IsMasked;

    public string? EarnedText => IsEarned && Progress.EarnedOn is { } on
        ? (Badge.IsTiered ? "Reached on " : "Earned on ") + on.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
        : null;

    /// <summary>The detail's ladder: each tier with its threshold, reached or not.</summary>
    public IReadOnlyList<BadgeTierRow> Tiers => Badge.IsTiered && !IsMasked
        ? Enumerable.Range(1, Badge.Tiers.Count).Select(t => new BadgeTierRow(
            BadgeCatalog.TierName(Badge, t)!, BadgeDisplay.Threshold(Badge, t, Imperial), t <= Tier,
            BadgeLooks.For(Badge, true, t), BadgeCatalog.GlyphFor(Badge, t) ?? "", Badge.Text)).ToList()
        : [];

    public bool HasTiers => Tiers.Count > 0;
}

/// <summary>A featured badge as it travels (<c>"streak:3"</c>, Phase 25), ready to draw: its glyph or text and look.
/// Null for none, a malformed value or a badge this version doesn't know.</summary>
public sealed record FeaturedLook(string Id, string Name, string Glyph, string? Text, BadgeLook Look)
{
    /// <summary>What to draw for a featured badge (<c>"&lt;id&gt;:&lt;tier&gt;"</c>). A hidden badge the viewer hasn't
    /// earned (<paramref name="viewerHas"/> says no, or isn't known) shows as the dashed "?", named "Secret badge", so
    /// featuring one never spoils it for friends.</summary>
    public static FeaturedLook? From(string? featured, Func<string, bool>? viewerHas = null)
    {
        if (FeaturedBadge.Parse(featured) is not var (id, tier) || BadgeCatalog.Find(id) is not { } badge) return null;
        if (badge.Hidden && viewerHas?.Invoke(id) != true) return new FeaturedLook(id, SecretName, "", "?", BadgeLook.Secret);
        return new FeaturedLook(id, badge.Name, BadgeCatalog.GlyphFor(badge, Math.Max(tier, 1)) ?? "", badge.Text,
            BadgeLooks.For(badge, true, tier));
    }

    public const string SecretName = "Secret badge";
    public bool IsSecret => Look == BadgeLook.Secret;
}

public sealed record BadgeTierRow(string Name, string Threshold, bool Reached, BadgeLook Look, string Glyph, string? Text);

/// <summary>Builds the screens' badge lists from what <see cref="BadgeService"/> returns.</summary>
public static class BadgeItems
{
    /// <summary>Every badge in catalog order; of the monthly ones, the current month's and the earned ones.</summary>
    public static List<BadgeItemViewModel> From(IReadOnlyList<BadgeProgress> progress, bool imperial, DateOnly today)
    {
        var current = MonthlyChallenges.IdFor(today);
        return progress
            .Where(p => !p.Id.StartsWith("monthly-", StringComparison.Ordinal) || p.Earned || p.Id == current)
            .Select(p => BadgeCatalog.Find(p.Id) is { } def ? new BadgeItemViewModel(def, p, imperial) : null)
            .OfType<BadgeItemViewModel>()
            .OrderBy(i => i.Badge.Hidden)
            .ThenBy(i => Order(i.Id))
            .ToList();
    }

    /// <summary>Catalog order; the monthly badges go right after Top of the Board, the last social badge.</summary>
    private static double Order(string id)
    {
        var monthly = id.StartsWith("monthly-", StringComparison.Ordinal);
        var key = monthly ? "top-of-the-board" : id;
        for (var i = 0; i < BadgeCatalog.All.Count; i++)
            if (BadgeCatalog.All[i].Id == key) return monthly ? i + 0.5 : i;
        return double.MaxValue;
    }

    /// <summary>How many badges there are to earn: the catalog plus this month's challenge.</summary>
    public static int Total => BadgeCatalog.All.Count + 1;
}

/// <summary>The badges screen (Phase 25, "Phone · Achievements"): how many are earned, the closest next one, an
/// All / Earned / Locked toggle and the grid. A tile opens its detail.</summary>
public partial class BadgesViewModel : ViewModelBase
{
    private readonly BadgeService _badges;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;
    private List<BadgeItemViewModel> _all = [];

    public ObservableCollection<BadgeItemViewModel> Shown { get; } = [];

    [ObservableProperty] public partial bool IsLoading { get; set; } = true;
    [ObservableProperty] public partial int EarnedCount { get; set; }
    [ObservableProperty] public partial double EarnedRatio { get; set; }
    [ObservableProperty] public partial BadgeItemViewModel? Closest { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(EmptyText))]
    public partial int FilterIndex { get; set; }

    public int Total => BadgeItems.Total;
    public string EarnedText => $"of {Total} earned";
    public bool IsEmpty => !IsLoading && Shown.Count == 0;
    public string EmptyText => FilterIndex == 1 ? "No badges yet. Your first workout earns one soon." : "Every badge is earned.";

    public BadgesViewModel(BadgeService badges, SessionService session, Func<MainViewModel> shell)
    {
        _badges = badges;
        _session = session;
        _shell = shell;
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (_session.CurrentUser is not { } user) return;
        var today = AppClock.Today;
        var settings = await _badges.GetSettingsAsync(user.Id);
        _all = BadgeItems.From(await _badges.GetProgressAsync(user.Id, today), settings.Imperial, today);
        EarnedCount = _all.Count(i => i.IsEarned);
        EarnedRatio = Total == 0 ? 0 : (double)EarnedCount / Total;
        Closest = _all.Where(i => !i.IsMasked && i.ShowsProgress && !i.IsEarned).MaxBy(i => i.Ratio)
                  ?? _all.Where(i => !i.IsMasked && i.ShowsProgress).MaxBy(i => i.Ratio);
        IsLoading = false;
        Filter();
    }

    partial void OnFilterIndexChanged(int value) => Filter();

    private void Filter()
    {
        Shown.Clear();
        foreach (var i in _all.Where(i => FilterIndex switch { 1 => i.IsEarned, 2 => !i.IsEarned, _ => true }))
            Shown.Add(i);
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand] private void ShowAll() => FilterIndex = 0;
    [RelayCommand] private void ShowEarned() => FilterIndex = 1;
    [RelayCommand] private void ShowLocked() => FilterIndex = 2;
    [RelayCommand] private void Open(BadgeItemViewModel item) => _shell().OpenBadgeDetail(item);
    [RelayCommand] private void Back() => _shell().GoBack();
}

/// <summary>One badge's detail (a sheet on phones): big badge, rule or hint, the tier ladder and the progress.</summary>
public partial class BadgeDetailViewModel(Func<MainViewModel> shell) : ViewModelBase
{
    [ObservableProperty] public partial BadgeItemViewModel? Item { get; set; }

    public void Load(BadgeItemViewModel item) => Item = item;

    [RelayCommand] private void Close() => shell().ClosePopup();
}

/// <summary>
/// The unlock sheet (Phase 25, "Phone · Unlock moment"): one new badge or tier at a time, newest first, with Share and
/// Next / Done. In history mode it is the one "You earned N badges from your history" sheet. Closing marks what it
/// showed as seen.
/// </summary>
public partial class BadgeUnlockViewModel : ViewModelBase
{
    private readonly BadgeService _badges;
    private readonly SessionService _session;
    private readonly IPlatform _platform;
    private readonly Func<MainViewModel> _shell;
    private List<BadgeItemViewModel> _items = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Current), nameof(Eyebrow), nameof(Headline), nameof(Line), nameof(NextLabel), nameof(CountText),
        nameof(HasMore))]
    public partial int Index { get; set; }

    [ObservableProperty] public partial bool IsHistory { get; set; }
    [ObservableProperty] public partial int HistoryCount { get; set; }

    public IReadOnlyList<BadgeItemViewModel> Preview => _items.Take(6).ToList();
    public BadgeItemViewModel? Current => Index < _items.Count ? _items[Index] : null;
    public bool HasMore => !IsHistory && Index < _items.Count - 1;
    public bool CanShare => _platform.CanShare && !IsHistory && Current is not null;
    public string CountText => _items.Count > 1 && !IsHistory ? $"{Index + 1} of {_items.Count}" : "";

    public string Eyebrow => IsHistory ? "WELCOME TO BADGES"
        : Current is { Badge.Hidden: true } ? "SECRET BADGE FOUND"
        : Current is { Badge.IsTiered: true, Tier: > 1 } ? "NEW TIER UNLOCKED"
        : "BADGE UNLOCKED";

    public string Headline => IsHistory
        ? $"You earned {HistoryCount} badge{(HistoryCount == 1 ? "" : "s")} from your history"
        : Current is null ? "" : Current.Badge.IsTiered && Current.Badge.TierNames is null
            ? $"{Current.Badge.Name} · {BadgeDisplay.Threshold(Current.Badge, Current.Tier, Current.Imperial)}"
            : Current.Badge.IsTiered ? $"{Current.Badge.Name} · {BadgeCatalog.TierName(Current.Badge, Current.Tier)}"
            : Current.Badge.Name;

    public string Line => IsHistory
        ? "Everything you logged before badges came along counts. New ones show up here as you earn them."
        : Current is null ? ""
        : Current.Badge.IsTiered && Current.Tier < BadgeCatalog.MaxTier
            ? $"{BadgeCatalog.TierName(Current.Badge, Current.Tier)} reached. Next: {BadgeCatalog.TierName(Current.Badge, Current.Tier + 1)} at {BadgeDisplay.Threshold(Current.Badge, Current.Tier + 1, Current.Imperial)}."
            : Current.Badge.IsTiered ? "The top tier. Nothing left to climb here."
            : Current.Badge.Rule;

    public string NextLabel => HasMore ? "Next" : IsHistory ? "See my badges" : "Done";

    public BadgeUnlockViewModel(BadgeService badges, SessionService session, IPlatform platform, Func<MainViewModel> shell)
    {
        _badges = badges;
        _session = session;
        _platform = platform;
        _shell = shell;
    }

    /// <summary>The unlocks, newest first.</summary>
    public void Load(IReadOnlyList<BadgeItemViewModel> items)
    {
        _items = items.ToList();
        IsHistory = false;
        Index = 0;
        OnPropertyChanged(nameof(CanShare));
    }

    /// <summary>The history sheet: how many, and a few of them to show.</summary>
    public void LoadHistory(int count, IReadOnlyList<BadgeItemViewModel> earned)
    {
        _items = earned.OrderByDescending(i => i.Tier).ThenByDescending(i => i.Progress.EarnedOn).ToList();
        HistoryCount = count;
        IsHistory = true;
        Index = 0;
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(CanShare));
        OnPropertyChanged(nameof(Headline));
        OnPropertyChanged(nameof(Line));
        OnPropertyChanged(nameof(Eyebrow));
        OnPropertyChanged(nameof(NextLabel));
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        if (HasMore) { Index++; OnPropertyChanged(nameof(CanShare)); return; }
        await CloseAsync();
        if (IsHistory) _shell().ShowBadges();
    }

    [RelayCommand]
    private void Share()
    {
        if (Current is { } c) _platform.Share($"I just earned {Headline} on GainStreak.");
    }

    [RelayCommand] private Task CloseAsync() => FinishAsync();

    private async Task FinishAsync()
    {
        _shell().ClosePopup();
        if (!IsHistory && _session.CurrentUser is { } user && _items.Count > 0)
            await _badges.MarkSeenAsync(user.Id, _items.Select(i => i.Id));
    }
}

/// <summary>One choice in the featured-badge picker: an earned badge, or None.</summary>
public partial class FeaturedChoice(BadgeItemViewModel? item) : ObservableObject
{
    public BadgeItemViewModel? Item { get; } = item;
    public bool IsNone => Item is null;
    public string Name => Item?.Badge.Name ?? "None";
    [ObservableProperty] public partial bool IsSelected { get; set; }
}

/// <summary>The featured-badge picker (Phase 25, "Profile · Pick a featured badge"): the earned badges and None, with a
/// preview of a map pin and a leaderboard row.</summary>
public partial class FeaturedBadgePickerViewModel : ViewModelBase
{
    private readonly BadgeService _badges;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;
    private Func<Task> _saved = () => Task.CompletedTask;

    public ObservableCollection<FeaturedChoice> Choices { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPick), nameof(SaveLabel), nameof(IsSecretPick))]
    public partial FeaturedChoice? Selected { get; set; }

    /// <summary>A hidden badge picked: friends who haven't earned it see the "?" (<see cref="FeaturedLook"/>), and the
    /// picker says so.</summary>
    public bool IsSecretPick => Selected?.Item?.Badge.Hidden == true;
    public const string SecretNoteText =
        "This is a secret badge. Friends who haven't earned it will only see a \"?\", not the badge itself.";
    public string SecretNote => SecretNoteText;

    [ObservableProperty] public partial string Initials { get; set; } = "";
    [ObservableProperty] public partial string? Avatar { get; set; }
    [ObservableProperty] public partial string Username { get; set; } = "";

    public bool HasPick => Selected?.Item is not null;
    public string SaveLabel => Selected?.Item is { } i ? $"Feature {i.Badge.Name}" : "Show no badge";

    private readonly BadgeSync? _sync;

    public FeaturedBadgePickerViewModel(BadgeService badges, SessionService session, Func<MainViewModel> shell, BadgeSync? sync = null)
    {
        _badges = badges;
        _session = session;
        _shell = shell;
        _sync = sync;
    }

    public async Task LoadAsync(string initials, string? avatar, Func<Task> saved)
    {
        _saved = saved;
        Initials = initials;
        Avatar = avatar;
        if (_session.CurrentUser is not { } user) return;
        Username = user.Username;
        var today = AppClock.Today;
        var settings = await _badges.GetSettingsAsync(user.Id);
        var earned = BadgeItems.From(await _badges.GetProgressAsync(user.Id, today), settings.Imperial, today).Where(i => i.IsEarned);
        Choices.Clear();
        foreach (var item in earned) Choices.Add(new FeaturedChoice(item));
        Choices.Add(new FeaturedChoice(null));
        Select(settings.ShowsNone ? Choices[^1] : Choices.FirstOrDefault(c => c.Item?.Id == settings.FeaturedId) ?? Choices[^1]);
    }

    [RelayCommand]
    private void Select(FeaturedChoice choice)
    {
        foreach (var c in Choices) c.IsSelected = c == choice;
        Selected = choice;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_session.CurrentUser is not { } user) return;
        await _badges.SetFeaturedAsync(user.Id, Selected?.Item?.Id ?? BadgeService.NoFeatured);
        _sync?.MarkChanged();
        _shell().ClosePopup();
        await _saved();
    }

    [RelayCommand] private void Cancel() => _shell().ClosePopup();
}

/// <summary>Profile's Badges card (Phase 25, "Profile · Your badges"): the count, See all, the featured badge with
/// Change, the last four earned and the "Friends can see my badges" switch. Phones only.</summary>
public partial class ProfileBadgesViewModel : ViewModelBase
{
    private readonly BadgeService _badges;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;
    private bool _loading;

    public bool IsSupported { get; }

    public ObservableCollection<BadgeItemViewModel> Recent { get; } = [];

    [ObservableProperty] public partial BadgeItemViewModel? Featured { get; set; }
    [ObservableProperty] public partial string CountText { get; set; } = "";
    [ObservableProperty] public partial string FeaturedNote { get; set; } = "";
    [ObservableProperty] public partial bool Share { get; set; } = true;

    public bool HasFeatured => Featured is not null;
    public bool HasRecent => Recent.Count > 0;

    /// <summary>The avatar and username the picker previews with; Profile sets them.</summary>
    public string Initials { get; set; } = "";
    public string? Avatar { get; set; }

    private readonly BadgeSync? _sync;

    public ProfileBadgesViewModel(BadgeService badges, SessionService session, IPlatform platform, Func<MainViewModel> shell,
        BadgeSync? sync = null)
    {
        _badges = badges;
        _session = session;
        _shell = shell;
        _sync = sync;
        IsSupported = platform.HasBadges;
    }

    public async Task LoadAsync()
    {
        if (!IsSupported || _session.CurrentUser is not { } user) return;
        var today = AppClock.Today;
        var settings = await _badges.GetSettingsAsync(user.Id);
        var items = BadgeItems.From(await _badges.GetProgressAsync(user.Id, today), settings.Imperial, today);
        var earned = items.Where(i => i.IsEarned).ToList();
        CountText = $"{earned.Count} / {BadgeItems.Total}";
        Featured = settings.FeaturedId is { } id ? items.FirstOrDefault(i => i.Id == id) : null;
        FeaturedNote = Featured is null
            ? settings.ShowsNone ? "You chose to show no badge" : "Earn a badge to show it here"
            : settings.IsAutomatic ? "Picked for you: your highest tier. Shown next to your name on the map and leaderboard"
            : "Shown next to your name on the map and leaderboard";
        Recent.Clear();
        foreach (var i in earned.OrderByDescending(i => i.Progress.EarnedOn).Take(4)) Recent.Add(i);
        _loading = true;
        Share = settings.Share;
        _loading = false;
        OnPropertyChanged(nameof(HasFeatured));
        OnPropertyChanged(nameof(HasRecent));
    }

    partial void OnShareChanged(bool value)
    {
        if (_loading || _session.CurrentUser is not { } user) return;
        _ = SaveShareAsync(user.Id, value);
    }

    private async Task SaveShareAsync(int userId, bool share)
    {
        await _badges.SetShareAsync(userId, share);
        _sync?.MarkChanged();
    }

    [RelayCommand] private void SeeAll() => _shell().ShowBadges();

    [RelayCommand] private void Open(BadgeItemViewModel item) => _shell().OpenBadgeDetail(item);

    [RelayCommand] private void Change() => _shell().OpenFeaturedPicker(Initials, Avatar, LoadAsync);
}

/// <summary>One of a friend's badges, with how it compares to the viewer's: "You: Lime" for a tiered badge you have,
/// "You:" with a green check for a one-time badge you have, "You:" with a red cross for one you don't. A hidden badge
/// you haven't earned shows its hint instead ("Trained while the city slept."). <paramref name="myTier"/> is your tier's name when you have
/// a tiered badge, else null.</summary>
public sealed class FriendBadgeTile(BadgeItemViewModel item, bool both, bool masked, string? myTier)
{
    public BadgeItemViewModel Item { get; } = item;
    public bool Both { get; } = both;

    /// <summary>The words under the badge; a check or cross follows when <see cref="ShowsCheck"/> / <see cref="ShowsCross"/>.</summary>
    public string Label => masked ? Item.Badge.Hint ?? "Earn it to see it" : Both && myTier is not null ? $"You: {myTier}" : "You:";
    public bool ShowsCheck => !masked && Both && myTier is null;
    public bool ShowsCross => !masked && !Both;

    /// <summary>The note as one string, for screen readers: "You: Lime", "You: yes", "You: no", or a hidden badge's hint.</summary>
    public string Note => ShowsCheck ? "You: yes" : ShowsCross ? "You: no" : Label;
}

/// <summary>
/// A friend's badges (Phase 25e, "Profile · A friend's badges"): their featured badge, how many they have and how many
/// you share, an All / Both have / Only &lt;name&gt; toggle and the grid. A hidden badge you haven't earned stays a "?".
/// Opened from a leaderboard row, the friends list or the map. Offline, the cached copy with a note.
/// </summary>
public partial class FriendBadgesViewModel : ViewModelBase
{
    private readonly IBadgeShareService _share;
    private readonly BadgeService _badges;
    private readonly SessionService _session;
    private readonly Func<MainViewModel> _shell;
    private readonly ILeaderboardService? _board;
    private List<FriendBadgeTile> _all = [];

    public ObservableCollection<FriendBadgeTile> Shown { get; } = [];

    [ObservableProperty] public partial string Username { get; set; } = "";
    [ObservableProperty] public partial string Initials { get; set; } = "";
    [ObservableProperty] public partial string? Avatar { get; set; }
    [ObservableProperty] public partial int Level { get; set; }
    [ObservableProperty] public partial FeaturedLook? Featured { get; set; }
    [ObservableProperty] public partial string CountText { get; set; } = "";
    [ObservableProperty] public partial string CommonText { get; set; } = "";
    [ObservableProperty] public partial string? Notice { get; set; }
    [ObservableProperty] public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial int FilterIndex { get; set; }

    public string Title => $"{Username}'s badges";
    /// <summary>"Only mara"; a longer name doesn't fit the pill's third of the width: "Only them".</summary>
    public string OnlyLabel => Username.Length <= 8 ? $"Only {Username}" : "Only them";
    public bool HasFeatured => Featured is not null;
    public bool IsEmpty => !IsLoading && Shown.Count == 0;
    public string EmptyText => _all.Count == 0
        ? $"{Username} has no badges to show yet."
        : FilterIndex == 1 ? "No badges in common yet." : "Nothing here.";

    public FriendBadgesViewModel(IBadgeShareService share, BadgeService badges, SessionService session, Func<MainViewModel> shell,
        ILeaderboardService? board = null)
    {
        _share = share;
        _badges = badges;
        _session = session;
        _shell = shell;
        _board = board;
    }

    /// <summary><paramref name="friendId"/> is the id the friend services use (a server id when signed in to the server).
    /// A <paramref name="level"/> of 0 means the caller doesn't know it (the map): the level ring and avatar then come
    /// from the friend's leaderboard row.</summary>
    public async Task LoadAsync(int friendId, string username, string? avatar, int level, string? featured)
    {
        Username = username;
        Initials = ViewModels.Initials.Of(username);
        Avatar = avatar;
        Level = level;
        Featured = FeaturedLook.From(featured);
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(OnlyLabel));
        OnPropertyChanged(nameof(HasFeatured));
        if (_session.CurrentUser is not { } user) return;
        try
        {
            var today = AppClock.Today;
            if (level <= 0) await FillInFromBoardAsync(user.Id, friendId, today);
            var settings = await _badges.GetSettingsAsync(user.Id);
            var mine = (await _badges.GetProgressAsync(user.Id, today)).ToDictionary(p => p.Id);
            var theirs = await _share.GetFriendBadgesAsync(user.Id, friendId);
            if (theirs is null)
            {
                Notice = $"Couldn't load {username}'s badges. Check your connection and try again.";
                return;
            }
            if (theirs.IsStale) Notice = "Offline: showing what was last fetched.";

            var progress = theirs.Badges.Select(b => new BadgeProgress(b.Id, true, b.Tier, b.EarnedOn, 0, null)).ToList();
            _all = BadgeItems.From(progress, settings.Imperial, today)
                .Select(i =>
                {
                    var my = mine.GetValueOrDefault(i.Id);
                    var both = my is { Earned: true };
                    var masked = i.Badge.Hidden && !both;
                    var item = masked ? new BadgeItemViewModel(i.Badge, i.Progress, settings.Imperial, masked: true) : i;
                    var myTier = both && i.Badge.IsTiered ? BadgeCatalog.TierName(i.Badge, my!.Tier) : null;
                    return new FriendBadgeTile(item, both, masked, myTier);
                })
                .ToList();
            // Now that your own badges are known, a hidden featured badge you've earned shows as itself
            bool YouHave(string badgeId) => mine.GetValueOrDefault(badgeId) is { Earned: true };
            Featured = FeaturedLook.From(featured, YouHave);
            if (Featured is null && theirs.Featured is { } id && theirs.Badges.FirstOrDefault(b => b.Id == id) is { } f)
                Featured = FeaturedLook.From(FeaturedBadge.Format(f.Id, f.Tier), YouHave);
            OnPropertyChanged(nameof(HasFeatured));
            CountText = $"{_all.Count} / {BadgeItems.Total}";
            CommonText = $"{_all.Count(t => t.Both)} in common";
        }
        finally
        {
            IsLoading = false;
            Filter();
        }
    }

    partial void OnFilterIndexChanged(int value) => Filter();

    /// <summary>The level and avatar from the friend's all-time leaderboard row (cached when offline). Left as they are
    /// if the board can't be read or the friend isn't on it.</summary>
    private async Task FillInFromBoardAsync(int userId, int friendId, DateOnly today)
    {
        if (_board is null) return;
        try
        {
            var view = await _board.GetAsync(userId, LeaderboardPeriod.AllTime, today);
            if (view?.Rows.FirstOrDefault(r => r.UserId == friendId) is not { } row) return;
            Level = row.Level.Number;
            Avatar ??= row.Avatar;
        }
        catch (Exception)
        {
            // the ring is a nicety: the badges still load without it
        }
    }

    private void Filter()
    {
        Shown.Clear();
        foreach (var t in _all.Where(t => FilterIndex switch { 1 => t.Both, 2 => !t.Both, _ => true })) Shown.Add(t);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }

    [RelayCommand] private void ShowAll() => FilterIndex = 0;
    [RelayCommand] private void ShowBoth() => FilterIndex = 1;
    [RelayCommand] private void ShowOnly() => FilterIndex = 2;
    [RelayCommand] private void Open(FriendBadgeTile tile) => _shell().OpenBadgeDetail(tile.Item);
    [RelayCommand] private void Back() => _shell().GoBack();
}
