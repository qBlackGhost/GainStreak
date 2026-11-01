using GymApp.App.ViewModels;

namespace GymApp.Tests;

public class PasswordVisibilityTests
{
    [Fact] public void StartsHidden()
    {
        var view = new PasswordVisibility();

        Assert.Equal('●', view.Mask);
        Assert.Equal("Show password", view.Label);
    }

    [Fact] public void Toggle_ShowsAndHidesAgain()
    {
        var view = new PasswordVisibility();

        view.ToggleCommand.Execute(null);
        Assert.Equal(default, view.Mask);
        Assert.Equal("Hide password", view.Label);

        view.ToggleCommand.Execute(null);
        Assert.Equal('●', view.Mask);
        Assert.Equal("Show password", view.Label);
    }

    [Fact] public void Toggle_TellsTheBoxes()
    {
        var view = new PasswordVisibility();
        var changed = new List<string?>();
        view.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        view.ToggleCommand.Execute(null);

        Assert.Contains(nameof(PasswordVisibility.Mask), changed);
        Assert.Contains(nameof(PasswordVisibility.Label), changed);
    }
}
