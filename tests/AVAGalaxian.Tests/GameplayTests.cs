using AVAGalaxian.Engine;

namespace AVAGalaxian.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avagalaxian-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A game in play with the swarm in its places (or none, if a test wants to put its own), and nothing diving or firing on its own.</summary>
    private static GameEngine Game(int wave = 1, bool keepSwarm = false)
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(wave);
        if (!keepSwarm) g.ClearAliens();
        return g;
    }

    /// <summary>A real game in play, with plenty of fighters.</summary>
    private static GameEngine Real(int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(wave);
        g.SetLives(99);
        return g;
    }

    private static readonly Controls FireButton = new(Fire: true);

    /// <summary>Fires and runs until the missile is gone.</summary>
    private static void Shoot(GameEngine g)
    {
        g.Tick(FireButton);
        for (int i = 0; i < 120 && g.Missile != null; i++) g.Tick(default);
    }

    /// <summary>Lines up under an alien in the formation, leading it as it sways, and shoots.</summary>
    private static void ShootAt(GameEngine g, Alien a)
    {
        g.Tick(default);
        double t = (Field.ShipY - 20 - a.Y) / GameEngine.MissileSpeed;
        g.SetShipX(a.X + a.Vx * t);
        Shoot(g);
    }

    // ───────────────────────────── the swarm ─────────────────────────────

    [Fact]
    public void The_swarm_is_two_commanders_six_guards_eight_stingers_and_thirty_drones()
    {
        var g = Game(keepSwarm: true);
        Assert.Equal(46, g.Aliens.Count);
        Assert.Equal(2, g.Aliens.Count(a => a.Kind == AlienKind.Commander));
        Assert.Equal(6, g.Aliens.Count(a => a.Kind == AlienKind.Guard));
        Assert.Equal(8, g.Aliens.Count(a => a.Kind == AlienKind.Stinger));
        Assert.Equal(30, g.Aliens.Count(a => a.Kind == AlienKind.Drone));
        // The commanders sit above the guards, the guards above the stingers, the stingers above the drones.
        Assert.True(g.Aliens.Where(a => a.Kind == AlienKind.Commander).Max(a => a.Y) < g.Aliens.Where(a => a.Kind == AlienKind.Guard).Min(a => a.Y));
        Assert.True(g.Aliens.Where(a => a.Kind == AlienKind.Stinger).Max(a => a.Y) < g.Aliens.Where(a => a.Kind == AlienKind.Drone).Min(a => a.Y));
    }

    [Fact]
    public void The_swarm_flies_in_from_above_and_is_in_its_places_when_play_begins()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame();
        Assert.Equal(Phase.Start, g.Phase);
        Assert.All(g.Aliens, a => Assert.True(a.Y < Field.Top));
        for (int i = 0; i < 600 && g.Phase == Phase.Start; i++) g.Tick(default);
        Assert.Equal(Phase.Play, g.Phase);
        Assert.All(g.Aliens, a => Assert.Equal(AlienState.Holding, a.State));
    }

    [Fact]
    public void The_formation_sways_from_side_to_side()
    {
        var g = Game(keepSwarm: true);
        double x0 = g.Aliens[0].X;
        g.Idle(120);
        Assert.NotEqual(x0, g.Aliens[0].X, 1);
        Assert.All(g.Aliens, a => Assert.InRange(a.X, Field.Left, Field.Right));
    }

    // ───────────────────────────── the missile ─────────────────────────────

    [Fact]
    public void Only_one_missile_is_in_the_air_at_a_time_and_each_takes_a_fresh_press()
    {
        var g = Game();
        g.Tick(FireButton);
        Assert.NotNull(g.Missile);
        var first = g.Missile;
        g.Tick(default);
        g.Tick(FireButton);
        Assert.Same(first, g.Missile);
        // Holding the button down doesn't fire again once the missile is gone.
        g.Run(120, FireButton);
        Assert.Null(g.Missile);
        g.Tick(default);
        g.Tick(FireButton);
        Assert.NotNull(g.Missile);
    }

    [Fact]
    public void A_drone_in_its_place_is_worth_30_and_one_on_the_wing_60()
    {
        var g = Game();
        ShootAt(g, g.PutAlien(AlienKind.Drone, 4, 5));
        Assert.Equal(30, g.Score);

        var h = Game();
        var a = h.PutAlien(AlienKind.Drone, 4, 5);
        a.State = AlienState.Diving;
        a.T = 5;
        (a.X, a.Y, a.Vx, a.Vy, a.TargetX) = (h.Ship.X, 400, 0, 0.001, h.Ship.X);
        h.Tick(FireButton);
        h.Run(30, default);
        Assert.True(a.Dead);
        Assert.Equal(60, h.Score);
    }

    [Theory]
    [InlineData(AlienKind.Stinger, 40, 80)]
    [InlineData(AlienKind.Guard, 50, 100)]
    [InlineData(AlienKind.Commander, 60, 150)]
    public void The_others_are_worth_more_and_double_or_more_on_the_wing(AlienKind kind, int holding, int diving)
    {
        var a = new Alien { Kind = kind };
        Assert.Equal(holding, GameEngine.Points(a));
        a.State = AlienState.Diving;
        Assert.Equal(diving, GameEngine.Points(a));
    }

    // ───────────────────────────── commanders and escorts ─────────────────────────────

    private static (GameEngine G, Alien Commander) EscortedDive()
    {
        var g = Game(keepSwarm: true);
        var c = g.Aliens.First(a => a.Kind == AlienKind.Commander);
        g.SendDive(c, escorted: true);
        return (g, c);
    }

    [Fact]
    public void A_commander_dives_with_two_guards_from_beside_it_as_escort()
    {
        var (g, c) = EscortedDive();
        Assert.Equal(2, c.Escorts.Count);
        Assert.All(c.Escorts, e =>
        {
            Assert.Equal(AlienKind.Guard, e.Kind);
            Assert.Equal(AlienState.Escorting, e.State);
            Assert.InRange(Math.Abs(e.Col - c.Col), 0, 1);
        });
        // They fly with it, one to either side, a little behind.
        g.Idle(90);
        Assert.Equal(AlienState.Diving, c.State);
        Assert.Equal(0, c.Escorts.Sum(e => Math.Sign(e.OffsetX)));
        Assert.All(c.Escorts, e => Assert.InRange(Math.Sqrt((e.X - c.X) * (e.X - c.X) + (e.Y - c.Y) * (e.Y - c.Y)), 25, 45));
        Assert.True(Math.Abs(c.Escorts[0].X - c.Escorts[1].X) + Math.Abs(c.Escorts[0].Y - c.Escorts[1].Y) > 30);
    }

    [Fact]
    public void A_commander_is_worth_200_with_both_escorts_300_with_one_and_800_if_both_were_shot_first()
    {
        var (g, c) = EscortedDive();
        Assert.Equal(200, GameEngine.Points(c));
        c.Escorts[0].Dead = true;
        Assert.Equal(300, GameEngine.Points(c));
        c.Escorts[1].Dead = true;
        Assert.Equal(800, GameEngine.Points(c));
        c.EscortsAtStart = 1;
        Assert.Equal(400, GameEngine.Points(c));
    }

    [Fact]
    public void Shooting_the_escorts_and_then_the_commander_scores_the_big_bonus()
    {
        var (g, c) = EscortedDive();
        g.Idle(70);
        // Each escort in turn, put in the missile's path.
        foreach (var e in c.Escorts.ToList())
        {
            int before = g.Score;
            g.Tick(FireButton);
            for (int i = 0; i < 60 && g.Missile != null; i++)
            {
                (e.X, e.Y) = (g.Missile.X, g.Missile.Y - 20);
                g.Tick(default);
            }
            Assert.True(e.Dead);
            Assert.Equal(100, g.Score - before);
            g.Tick(default);
        }
        int then = g.Score;
        (c.X, c.Y) = (g.Ship.X, 400);
        g.Tick(FireButton);
        g.Run(40, default);
        Assert.True(c.Dead);
        Assert.Equal(800, g.Score - then);
        Assert.Contains(g.Effects, e => e.Text == "800");
    }

    [Fact]
    public void Escorts_carry_on_alone_when_their_commander_is_shot()
    {
        var (g, c) = EscortedDive();
        g.Idle(70);
        (c.X, c.Y) = (g.Ship.X, 400);
        g.Tick(FireButton);
        g.Run(40, default);
        Assert.True(c.Dead);
        Assert.Equal(200, g.Score);
        Assert.All(c.Escorts, e => Assert.Equal(AlienState.Diving, e.State));
    }

    // ───────────────────────────── dives ─────────────────────────────

    [Fact]
    public void A_diver_loops_out_comes_down_off_the_bottom_and_back_round_to_its_place()
    {
        var g = Game(keepSwarm: true);
        var a = g.Aliens.First(x => x.Kind == AlienKind.Drone && x.Col == 0);
        double homeY = a.Y;
        g.SendDive(a);
        g.SetShipX(Field.Right - 30);
        g.Idle(20);
        Assert.True(a.Y < homeY, "it rises first, into its loop");
        bool low = false;
        for (int i = 0; i < 60 * 10 && a.State != AlienState.Holding; i++)
        {
            g.Tick(default);
            if (a.Y > Field.ShipY - 40) low = true;
        }
        Assert.True(low, "it came down to the fighter's level");
        Assert.Equal(AlienState.Holding, a.State);
        Assert.Equal(homeY, a.Y, 1);
    }

    [Fact]
    public void Divers_fire_on_the_way_down()
    {
        var g = Real();
        int most = 0;
        for (int i = 0; i < 60 * 12; i++)
        {
            g.Tick(default);
            most = Math.Max(most, g.Bullets.Count);
        }
        Assert.True(most > 0);
    }

    [Fact]
    public void Aliens_peel_off_the_formation_by_themselves()
    {
        var g = Real();
        int flying = 0;
        for (int i = 0; i < 60 * 8; i++)
        {
            g.Tick(default);
            flying = Math.Max(flying, g.Aliens.Count(a => a.State == AlienState.Diving));
        }
        Assert.True(flying > 0);
    }

    [Fact]
    public void The_last_few_attack_without_a_break()
    {
        var g = Real();
        foreach (var a in g.Aliens.Skip(GameEngine.LastFew)) a.Dead = true;
        Assert.True(g.LastStand);
        int holdingTicks = 0;
        for (int i = 0; i < 60 * 10; i++)
        {
            g.Tick(default);
            if (!g.Ship.Alive) break;
            if (g.Aliens.Where(a => !a.Dead).All(a => a.State == AlienState.Holding)) holdingTicks++;
        }
        Assert.True(holdingTicks < 30);
    }

    // ───────────────────────────── losing a fighter ─────────────────────────────

    [Fact]
    public void A_bullet_brings_the_fighter_down_and_the_next_one_comes_after_a_moment()
    {
        var g = Game();
        g.PutAlien(AlienKind.Drone, 0, 5);
        int lives = g.Lives;
        g.PutBullet(g.Ship.X, Field.ShipY - 40);
        g.Idle(20);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("SHOT DOWN", g.Cause);
        Assert.Equal(lives - 1, g.Lives);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.Equal(Phase.Start, g.Phase);
        Assert.Equal("READY", g.Message);
        Assert.True(g.Ship.Alive);
        Assert.Equal(Field.CentreX, g.Ship.X);
    }

    [Fact]
    public void Being_rammed_takes_the_alien_down_too()
    {
        var g = Game();
        var a = g.PutAlien(AlienKind.Stinger, 4, 2);
        a.State = AlienState.Diving;
        a.T = 5;
        (a.X, a.Y, a.Vx, a.Vy) = (g.Ship.X, Field.ShipY - 10, 0, 1);
        g.Tick(default);
        Assert.Equal("RAMMED", g.Cause);
        Assert.True(a.Dead);
        Assert.Equal(80, g.Score);
    }

    [Fact]
    public void Divers_go_home_when_the_next_fighter_comes()
    {
        var g = Game(keepSwarm: true);
        var a = g.Aliens[10];
        g.SendDive(a);
        g.Idle(30);
        g.PutBullet(g.Ship.X, Field.ShipY - 20);
        g.Idle(5);
        Assert.Equal(Phase.Dying, g.Phase);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.NotEqual(AlienState.Diving, a.State);
    }

    [Fact]
    public void The_game_ends_with_the_last_fighter_and_a_good_score_gets_initials()
    {
        var g = Game();
        g.PutAlien(AlienKind.Drone, 0, 5);
        g.SetLives(1);
        g.SetScore(500000);
        g.PutBullet(g.Ship.X, Field.ShipY - 20);
        g.Idle(5);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.Equal(GameState.GameOver, g.State);
        g.Idle(4 * 60);
        Assert.Equal(GameState.EnterInitials, g.State);
    }

    // ───────────────────────────── waves and lives ─────────────────────────────

    [Fact]
    public void Destroying_the_swarm_brings_the_next_wave()
    {
        var g = Game();
        ShootAt(g, g.PutAlien(AlienKind.Drone, 4, 5));
        g.Calm = false;
        g.Tick(default);
        Assert.Equal(Phase.Clear, g.Phase);
        g.Idle((int)(GameEngine.ClearSeconds * 60) + 2);
        Assert.Equal(2, g.Wave);
        Assert.Equal(46, g.Aliens.Count);
    }

    [Fact]
    public void Later_waves_are_quicker()
    {
        Assert.True(Game(5).Pace > Game(1).Pace);
    }

    [Fact]
    public void An_extra_fighter_at_7000_and_every_30000_after()
    {
        var g = Game();
        int lives = g.Lives;
        for (int i = 0; i < 7000 / 30; i++)
        {
            ShootAt(g, g.PutAlien(AlienKind.Drone, 4, 5));
        }
        Assert.Equal(lives, g.Lives);
        Assert.Equal(6990, g.Score);
        ShootAt(g, g.PutAlien(AlienKind.Drone, 4, 5));
        Assert.Equal(lives + 1, g.Lives);
    }

    // ───────────────────────────── the machine ─────────────────────────────

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle((int)(GameEngine.AttractSeconds * 60) + 2);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_demonstration_pilot_shoots_aliens_down()
    {
        var g = Real();
        for (int i = 0; i < 60 * 30; i++) g.Tick(g.Pilot.Decide(g));
        Assert.True(g.Score > 500, $"score {g.Score}");
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = Real(seed: 3);
            for (int i = 0; i < 60 * 40; i++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Wave} {g.Lives} {g.Ship.X:0.00} {g.Remaining}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Pausing_stops_everything()
    {
        var g = Real();
        g.Idle(120);
        var before = string.Join(",", g.Aliens.Select(a => $"{a.X:0.0}"));
        g.Pause();
        g.Idle(120);
        Assert.Equal(GameState.Paused, g.State);
        Assert.Equal(before, string.Join(",", g.Aliens.Select(a => $"{a.X:0.0}")));
        g.Resume();
        Assert.Equal(GameState.Playing, g.State);
    }
}
