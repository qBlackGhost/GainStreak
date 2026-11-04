using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.Tests;

/// <summary>Phase 12d: the validation gaps found in the audit (docs/PHASE_12D_AUDIT.md) and the Field each
/// service message belongs to.</summary>
public class ValidationTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly AuthService _auth;

    public ValidationTests() => _auth = new AuthService(_db);

    public void Dispose() => _db.Dispose();

    // ===== G1: usernames =====

    [Theory]
    [InlineData("alice")]
    [InlineData("Ana_Maria-2.0")]
    [InlineData("Ștefan")]          // letters outside ASCII are fine
    public void Username_Allowed(string name) => Assert.Null(Credentials.CheckUsername(name));

    [Theory]
    [InlineData("a b")]
    [InlineData("tab\tname")]
    [InlineData("emoji💪")]
    [InlineData("‮admin")]          // right-to-left override
    [InlineData("semi;colon")]
    public void Username_Refused(string name) =>
        Assert.Equal("Use only letters, digits, '_', '.' and '-' in your username.", Credentials.CheckUsername(name));

    [Fact] public async Task Register_RefusesASpaceInTheUsername_OnTheUsernameField()
    {
        var r = await _auth.RegisterAsync("a b", "alice@example.com", "password1");
        Assert.False(r.Success);
        Assert.Equal(FormField.Username, r.Field);
    }

    [Fact] public async Task Register_TrimsBeforeChecking() =>
        Assert.True((await _auth.RegisterAsync("  alice  ", "alice@example.com", "password1")).Success);

    [Fact] public async Task Register_TakenName_IsOnTheUsernameField()
    {
        await _auth.RegisterAsync("alice", "alice@example.com", "password1");
        var r = await _auth.RegisterAsync("ALICE", "other@example.com", "password1");
        Assert.Equal(("That username is already taken.", FormField.Username), (r.Error, r.Field));
    }

    // ===== G2: passwords =====

    [Fact] public async Task Register_PasswordOver128_IsRefused_OnThePasswordField()
    {
        var r = await _auth.RegisterAsync("alice", "alice@example.com", new string('x', 129));
        Assert.Equal(("Password must be at most 128 characters.", FormField.Password), (r.Error, r.Field));
        Assert.True((await _auth.RegisterAsync("alice", "alice@example.com", new string('x', 128))).Success);
    }

    [Fact] public void Password_OnlySpaces_IsRefused() =>
        Assert.Equal("Password can't be only spaces.", Credentials.CheckPassword("          "));

    [Fact] public void Password_TooShort_IsRefused() =>
        Assert.Equal("Password must be at least 8 characters.", Credentials.CheckPassword("short"));

    [Fact] public async Task Login_StillWorks_ForAnOldUsernameTheNewRulesWouldRefuse()
    {
        using (var db = _db.CreateDbContext())
        {
            db.Users.Add(new User { Username = "old name", PasswordHash = PasswordHasher.Hash("password1") });
            db.SaveChanges();
        }
        Assert.True((await _auth.LoginAsync("old name", "password1")).Success);
    }

    // ===== Emails =====

    [Theory]
    [InlineData("alice@example.com")]
    [InlineData("ana.maria+gym@mail.co.uk")]
    public void Email_Allowed(string email) => Assert.Null(Credentials.CheckEmail(email));

    [Theory]
    [InlineData("alice")]
    [InlineData("@example.com")]
    [InlineData("alice@example")]
    [InlineData("alice@@example.com")]
    [InlineData("al ice@example.com")]
    [InlineData("alice@example.")]
    public void Email_Refused(string email) =>
        Assert.Equal("Enter a valid email address, e.g. name@example.com.", Credentials.CheckEmail(email));

    [Fact] public async Task Register_BadEmail_IsOnTheEmailField()
    {
        var r = await _auth.RegisterAsync("alice", "not-an-email", "password1");
        Assert.Equal(FormField.Email, r.Field);
    }

    [Fact] public async Task Register_TakenEmail_IgnoresCase()
    {
        await _auth.RegisterAsync("alice", "alice@example.com", "password1");
        var r = await _auth.RegisterAsync("bob", "  ALICE@Example.com ", "password1");
        Assert.Equal(("An account with that email already exists.", FormField.Email), (r.Error, r.Field));
    }

    [Fact] public async Task Login_WorksWithUsernameOrEmail()
    {
        await _auth.RegisterAsync("alice", "Alice@Example.com", "password1");
        Assert.True((await _auth.LoginAsync("alice", "password1")).Success);
        Assert.True((await _auth.LoginAsync(" ALICE@example.com ", "password1")).Success);
        Assert.False((await _auth.LoginAsync("alice@example.com", "wrong-password")).Success);
    }

    [Fact] public async Task ChangeEmail_NeedsThePassword_AndAFreeAddress()
    {
        var alice = (await _auth.RegisterAsync("alice", "alice@example.com", "password1")).User!;
        await _auth.RegisterAsync("bob", "bob@example.com", "password1");

        Assert.Equal(FormField.Password, (await _auth.ChangeEmailAsync(alice.Id, "new@example.com", "nope")).Field);
        Assert.Equal(FormField.Email, (await _auth.ChangeEmailAsync(alice.Id, "BOB@example.com", "password1")).Field);
        Assert.True((await _auth.ChangeEmailAsync(alice.Id, "New@Example.com", "password1")).Success);
        Assert.True((await _auth.LoginAsync("new@example.com", "password1")).Success);
    }

    [Fact] public async Task ChangeEmail_AddsOneToAnOldAccountWithout()
    {
        int id;
        using (var db = _db.CreateDbContext())
        {
            var u = new User { Username = "olduser", PasswordHash = PasswordHasher.Hash("password1") };
            db.Users.Add(u);
            db.SaveChanges();
            id = u.Id;
        }
        Assert.True((await _auth.ChangeEmailAsync(id, "old@example.com", "password1")).Success);
    }

    // ===== G4 / G5: whole numbers =====

    [Theory]
    [InlineData("2100", 2100)]
    [InlineData(" 2100 ", 2100)]
    [InlineData("2,100", 2100)]
    [InlineData("2.100", 2100)]
    [InlineData("2 100", 2100)]
    [InlineData("12,345,678", 12345678)]
    [InlineData("0", 0)]
    [InlineData("5", 5)]
    public void WholeNumber_Accepted(string text, int expected)
    {
        Assert.True(NumberParsing.TryParseWhole(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("210.5")]       // used to save as 2,105 kcal
    [InlineData("2,1")]         // used to save as 21
    [InlineData("1,00")]
    [InlineData("1,000.000")]   // two different separators
    [InlineData("2100,")]
    [InlineData("-5")]
    [InlineData("5.5")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("99999999999")] // too big for an int
    public void WholeNumber_Refused(string text) => Assert.False(NumberParsing.TryParseWhole(text, out _));

    [Fact] public void Units_WholeNumber_UsesTheSameRules() =>
        Assert.False(Units.TryParseWholeNumber("210.5", out _));

    // ===== G6: set weights are rounded on save (already true; kept as a check) =====

    [Fact] public async Task SetWeights_AreRoundedToTwoDecimals()
    {
        var alice = _db.AddUser("alice");
        var sets = new SetService(_db);
        var day = new DateOnly(2026, 10, 5);
        Assert.True((await sets.SaveDayAsync(alice, 1, [new SetInput(SetKind.Working, 5, 60.123456)], day)).Success);
        Assert.Equal(60.12, Assert.Single(await sets.GetDayAsync(alice, 1, day)).WeightKg);
    }

    // ===== G3: each message names its field =====

    [Fact] public async Task Profile_HeightOutOfRange_IsOnTheHeightField()
    {
        var alice = _db.AddUser("alice");
        var r = await new ProfileService(_db).SaveAsync(alice, new ProfileInput(null, null, 20, null, UnitSystem.Metric));
        Assert.Equal(FormField.Height, r.Field);
    }

    [Fact] public async Task Profile_BirthDateInTheFuture_IsOnTheBirthDateField()
    {
        var alice = _db.AddUser("alice");
        var today = new DateOnly(2026, 10, 18);
        var r = await new ProfileService(_db).SaveAsync(alice,
            new ProfileInput(null, today.AddDays(1), null, null, UnitSystem.Metric), today);
        Assert.Equal(FormField.BirthDate, r.Field);
    }

    [Fact] public async Task Split_NamesAndDays_AreOnTheirFields()
    {
        var alice = _db.AddUser("alice");
        var splits = new SplitService(_db, new PointsService(_db));
        Assert.Equal(FormField.Name, (await splits.CreateAsync(alice, " ", [new DayInput(DayOfWeek.Monday, "Push")])).Field);
        Assert.Equal(FormField.Days, (await splits.CreateAsync(alice, "PPL", [])).Field);
        Assert.Equal(FormField.Days, (await splits.CreateAsync(alice, "PPL", [new DayInput(DayOfWeek.Monday, " ")])).Field);
    }

    [Fact] public async Task Exercise_EmptyName_IsOnTheNameField()
    {
        var alice = _db.AddUser("alice");
        var r = await new ExerciseService(_db).CreateAsync(alice, "  ", MuscleGroup.Chest);
        Assert.Equal(FormField.Name, r.Field);
    }

    [Fact] public async Task CustomGym_NameAndAddress_AreOnTheirFields()
    {
        var alice = _db.AddUser("alice");
        int city;
        using (var db = _db.CreateDbContext())
        {
            var c = new City { OsmType = OsmElementType.Relation, OsmId = 1, Name = "Timișoara", DisplayName = "Timișoara" };
            db.Cities.Add(c);
            db.SaveChanges();
            city = c.Id;
        }
        var gyms = new GymService(_db);
        Assert.Equal(FormField.Name, (await gyms.AddCustomGymAsync(alice, city, " ", null)).Field);
        Assert.Equal(FormField.Address,
            (await gyms.AddCustomGymAsync(alice, city, "Gym", new PickedAddress("Strada X", double.NaN, 21))).Field);
        Assert.Equal(FormField.Address, (await gyms.AddCustomGymAsync(alice, city, "Gym", null)).Field);   // required (18c)
        var at = new PickedAddress("Strada X 1", 45.75, 21.22);
        Assert.True((await gyms.AddCustomGymAsync(alice, city, "Gym", at)).Success);
        Assert.Equal(FormField.Name, (await gyms.AddCustomGymAsync(alice, city, "gym", at)).Field);   // taken
    }

    [Fact] public async Task ModifyPr_EachLimit_IsOnItsField()
    {
        var alice = _db.AddUser("alice");
        var sets = new SetService(_db);
        Assert.Equal(FormField.Reps, (await sets.SetPrAsync(alice, 1, 100, 0)).Field);
        Assert.Equal(FormField.Weight, (await sets.SetPrAsync(alice, 1, 5000, 5)).Field);
        Assert.Equal(FormField.OneRepMax, (await sets.SetPrAsync(alice, 1, 100, 5, oneRepMaxKg: 50)).Field);
    }
}
