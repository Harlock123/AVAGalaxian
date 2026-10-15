using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAGalaxian.Engine;
using AVAGalaxian.Views;

namespace AVAGalaxian.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set GALAXIAN_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avagalaxian-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("GALAXIAN_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAGalaxian-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 24850, 9);
        g.HighScores.Insert("AVA", 15200, 6);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void The_swarm()
    {
        var (w, c, g) = Open();
        g.StartGame();
        g.SetLives(4);
        g.Idle(50);
        c.Advance(0.5);
        Shot(w, c, "flying-in");
        Assert.True(PlayUntil(g, c, g => g.WaveTime > 6 && g.Ship.Alive && g.Missile != null && g.Missile.Y < 500
                                       && g.Aliens.Count(a => a.State == AlienState.Diving && a.Y > 300) >= 2 && g.Bullets.Count >= 2));
        Shot(w, c, "swarm");
    }

    [AvaloniaFact]
    public void A_commander_and_its_escort()
    {
        var (w, c, g) = Open(4);
        g.Go(2);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.Ship.Alive && g.Aliens.Any(a => a.Kind == AlienKind.Commander && a.State == AlienState.Diving
                                       && a.Escorts.Count(e => !e.Dead && e.Leader == a) == 2 && a.Y > 330 && a.Y < 560)));
        Shot(w, c, "escort");
    }

    [AvaloniaFact]
    public void The_last_few()
    {
        var (w, c, g) = Open(5);
        g.Go(3);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.LastStand && g.Remaining >= 3 && g.Ship.Alive && g.Aliens.Count(a => !a.Dead && a.State == AlienState.Diving && a.Y > 250) >= 2, 200));
        Shot(w, c, "last-few");
    }

    [AvaloniaFact]
    public void A_fighter_lost()
    {
        var (w, c, g) = Open();
        g.Go(2);
        g.SetLives(3);
        Assert.True(PlayUntil(g, c, g => g.WaveTime > 5 && g.Ship.Alive));
        g.PutBullet(g.Ship.X, Field.ShipY - 20);
        g.Idle(30);
        c.Advance(0.4);
        Shot(w, c, "lost");
    }
}
