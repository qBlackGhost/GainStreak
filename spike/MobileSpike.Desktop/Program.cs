using Avalonia;
using MobileSpike;

SpikeLog.Mark("Main");
AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(args);
