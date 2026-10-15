namespace AVAGalaxian.Engine;

public sealed partial class GameEngine
{
    public const double ShipSpeed = 300, MissileSpeed = 760, BulletSpeed = 290;
    public const double StartSeconds = 2.4, ReadySeconds = 1.6, DyingSeconds = 2.2, ClearSeconds = 2.0;
    public const int FirstExtraLife = 7000, ExtraLifeEvery = 30000;
    /// <summary>When this few are left, they stop going home and attack without a break.</summary>
    public const int LastFew = 6;

    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Ship Ship { get; } = new();
    /// <summary>The fighter's one missile, while it's in the air.</summary>
    public Missile? Missile { get; private set; }
    public List<Bullet> Bullets { get; } = [];
    public List<Alien> Aliens { get; } = [];
    public ShipPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public double WaveTime { get; private set; }
    /// <summary>For tests: nothing fires or dives by itself, so a test can set up just what it wants.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra, _dives;
    private double _diveIn;
    private bool _firePrev, _ready;

    /// <summary>How hard the wave is: quicker dives, more of them, more bullets.</summary>
    public double Pace => Math.Min(1.7, 1 + 0.08 * (Wave - 1));
    /// <summary>The formation drifts from side to side.</summary>
    public double Sway => Math.Sin(WaveTime * 0.55) * 60;
    private double SwaySpeed => Math.Cos(WaveTime * 0.55) * 0.55 * 60;
    public int Remaining => Aliens.Count(a => !a.Dead);
    public bool LastStand => Remaining <= LastFew;
    private int MaxDivers => Math.Min(7, 2 + Wave / 2);
    private int MaxBullets => Math.Min(12, 5 + Wave);

    public static double HomeX(int col) => Field.CentreX + (col - (Field.Columns - 1) / 2.0) * Field.ColGap;
    public static double HomeY(int row) => Field.FormationTop + row * Field.RowGap;

    public static AlienKind KindOfRow(int row) => row switch
    {
        0 => AlienKind.Commander, 1 => AlienKind.Guard, 2 => AlienKind.Stinger, _ => AlienKind.Drone,
    };

    /// <summary>What an alien is worth: double (or more) on the wing.</summary>
    public static int Points(Alien a)
    {
        bool flying = a.State is not AlienState.Holding;
        return a.Kind switch
        {
            AlienKind.Drone => flying ? 60 : 30,
            AlienKind.Stinger => flying ? 80 : 40,
            AlienKind.Guard => flying ? 100 : 50,
            _ => !flying ? 60 : CommanderPoints(a),
        };
    }

    /// <summary>A diving commander: more the fewer of its escorts are left, and most of all if they were all shot first.</summary>
    public static int CommanderPoints(Alien a)
    {
        int alive = a.Escorts.Count(e => !e.Dead && e.Leader == a);
        if (a.EscortsAtStart == 0) return 150;
        if (alive == 0) return a.EscortsAtStart == 2 ? 800 : 400;
        return alive == 2 ? 200 : 300;
    }

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Wave = 1;
        _nextExtra = FirstExtraLife;
        LayOut();
        SetPhase(Phase.Start);
    }

    private void BeginWave(int wave)
    {
        Wave = wave;
        LayOut();
        SetPhase(Phase.Start);
        _ready = false;
        Say($"WAVE {wave}", wave == 1 ? "THE SWARM ARRIVES" : "ANOTHER SWARM", StartSeconds);
        Play(Sfx.WaveStart);
    }

    /// <summary>The formation, flying in from above to its places.</summary>
    private void LayOut()
    {
        Missile = null;
        Bullets.Clear();
        Aliens.Clear();
        Effects.Clear();
        WaveTime = 0;
        _dives = 0;
        _diveIn = 2.5;
        ResetShip();
        for (int row = 0; row < Field.Rows; row++)
            for (int col = 0; col < Field.Columns; col++)
            {
                bool there = row switch { 0 => col is 3 or 6, 1 => col is >= 2 and <= 7, 2 => col is >= 1 and <= 8, _ => true };
                if (!there) continue;
                var a = AddAlien(KindOfRow(row), col, row);
                a.Y = HomeY(row) - 460 - (Field.Rows - row) * 10 - Math.Abs(col - 4.5) * 14;
                a.State = AlienState.Returning;
            }
    }

    private Alien AddAlien(AlienKind kind, int col, int row)
    {
        var a = new Alien
        {
            Kind = kind, Col = col, Row = row, X = HomeX(col) + Sway, Y = HomeY(row), Phase = _rng.NextDouble() * Math.PI * 2,
        };
        Aliens.Add(a);
        return a;
    }

    private void ResetShip()
    {
        Ship.X = Field.CentreX;
        Ship.Alive = true;
    }

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        WaveTime += dt;
        switch (Phase)
        {
            case Phase.Start:
                MoveShip(c, dt, canFire: false);
                MoveAliens(dt, active: false);
                if (PhaseTime > (_ready ? ReadySeconds : StartSeconds))
                {
                    SetPhase(Phase.Play);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Play:
                MoveShip(c, dt, canFire: true);
                MoveMissile(dt);
                MoveAliens(dt, active: true);
                MoveBullets(dt);
                Hits();
                if (Phase == Phase.Play) Progress();
                break;
            case Phase.Dying:
                MoveMissile(dt);
                MoveAliens(dt, active: false);
                MoveBullets(dt);
                if (PhaseTime > DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Respawn();
                }
                break;
            case Phase.Clear:
                MoveBullets(dt);
                if (PhaseTime > ClearSeconds) BeginWave(Wave + 1);
                break;
        }
    }

    // ───────────────────────────── the fighter ─────────────────────────────

    private void MoveShip(Controls c, double dt, bool canFire)
    {
        bool fresh = c.Fire && !_firePrev;
        _firePrev = c.Fire;
        var s = Ship;
        if (!s.Alive) return;
        s.X = Math.Clamp(s.X + Math.Sign(c.Dx) * ShipSpeed * dt, Field.Left + 20, Field.Right - 20);
        // One missile at a time: it sits on the fighter's nose until it's fired.
        if (canFire && fresh && Missile == null)
        {
            Missile = new Missile { X = s.X, Y = Field.ShipY - 20 };
            Play(Sfx.Fire);
        }
    }

    private void MoveMissile(double dt)
    {
        if (Missile is not { } m) return;
        // In small steps, so it can't jump over an alien.
        for (int k = 0; k < 4 && !m.Dead; k++)
        {
            m.Y -= MissileSpeed * dt / 4;
            if (m.Y < Field.Top - 10) m.Dead = true;
            else if (Phase == Phase.Play)
                foreach (var a in Aliens)
                {
                    if (a.Dead || Math.Abs(a.X - m.X) > 15 || Math.Abs(a.Y - m.Y) > 13) continue;
                    KillAlien(a);
                    m.Dead = true;
                    break;
                }
        }
        if (m.Dead) Missile = null;
    }

    private void KillAlien(Alien a)
    {
        int points = Points(a);
        a.Dead = true;
        AddScore(points);
        bool big = a.Kind == AlienKind.Commander && a.State != AlienState.Holding;
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = a.X, Y = a.Y, Life = big ? 0.8 : 0.5, Size = big ? 1.5 : 1, Colour = Colour(a.Kind) });
        if (a.State != AlienState.Holding)
            Effects.Add(new Effect { Kind = EffectKind.Score, X = a.X, Y = a.Y - 14, Text = points.ToString(), Life = big ? 1.4 : 0.9, Colour = points >= 300 ? 0xFFFFE04A : 0xFFFFFFFF });
        Play(a.Kind == AlienKind.Commander ? (points >= 400 ? Sfx.Bonus : Sfx.CommanderHit) : Sfx.Hit);
        // A commander's escorts carry on alone.
        foreach (var e in a.Escorts.Where(e => !e.Dead && e.Leader == a))
        {
            e.Leader = null;
            e.State = AlienState.Diving;
        }
    }

    public static uint Colour(AlienKind k) => k switch
    {
        AlienKind.Commander => 0xFFFFD84A, AlienKind.Guard => 0xFFFF4A5A, AlienKind.Stinger => 0xFFD06AFF, _ => 0xFF4AA8FF,
    };

    // ───────────────────────────── the swarm ─────────────────────────────

    private void MoveAliens(double dt, bool active)
    {
        foreach (var a in Aliens)
        {
            if (a.Dead) continue;
            a.T += dt;
            switch (a.State)
            {
                case AlienState.Holding:
                    a.X = HomeX(a.Col) + Sway;
                    a.Y = HomeY(a.Row);
                    (a.Vx, a.Vy) = (SwaySpeed, 0);
                    a.Angle = 0;
                    break;
                case AlienState.Diving:
                    Dive(a, dt, active);
                    break;
                case AlienState.Escorting:
                    Escort(a, dt, active);
                    break;
                case AlienState.Returning:
                    Return(a, dt);
                    break;
            }
        }
        if (active && !Calm) Launch(dt);
    }

    /// <summary>Peel off with a loop up and out to the side, then down at the fighter, firing, and on off the bottom.</summary>
    private void Dive(Alien a, double dt, bool active)
    {
        double speed = DiveSpeed(a);
        double heading = Math.Atan2(a.Vy, a.Vx);
        if (a.T < LoopSeconds)
        {
            // The loop: from heading up to heading down, turning outwards.
            heading = -Math.PI / 2 + a.Side * Math.PI * a.T / LoopSeconds;
            speed = 170;
        }
        else if (a.Y < Field.ShipY - 130)
        {
            // Steer for where the fighter is, sweeping in an arc rather than turning on a pin.
            a.TargetX += Math.Clamp(Ship.X - a.TargetX, -160 * dt, 160 * dt);
            double want = Math.Atan2(Field.ShipY + 60 - a.Y, a.TargetX - a.X);
            double turn = Math.Atan2(Math.Sin(want - heading), Math.Cos(want - heading));
            heading += Math.Clamp(turn, -2.4 * dt, 2.4 * dt);
            // Always coming down.
            heading = Math.Clamp(heading, 0.35, Math.PI - 0.35);
        }
        (a.Vx, a.Vy) = (Math.Cos(heading) * speed, Math.Sin(heading) * speed);
        a.X += a.Vx * dt;
        a.Y += a.Vy * dt;
        // Bounce off the sides of the field rather than leave it.
        if (a.X < Field.Left + 14 && a.Vx < 0 || a.X > Field.Right - 14 && a.Vx > 0) { a.Vx = -a.Vx; a.X = Math.Clamp(a.X, Field.Left + 14, Field.Right - 14); }
        a.Angle = Math.Atan2(a.Vx, a.Vy);
        if (active) Shoot(a, dt);
        if (a.Y > Field.Bottom + 24) Wrap(a, active);
    }

    private const double LoopSeconds = 0.75;

    private double DiveSpeed(Alien a) => (a.Kind switch { AlienKind.Drone => 200, AlienKind.Stinger => 215, _ => 230 }) * Pace + Math.Min(a.T, 2) * 20;

    /// <summary>An escort keeps its place beside its commander.</summary>
    private void Escort(Alien a, double dt, bool active)
    {
        if (a.Leader is not { Dead: false } l || l.State is not AlienState.Diving)
        {
            a.Leader = null;
            a.State = AlienState.Diving;
            a.T = Math.Max(a.T, LoopSeconds);
            Dive(a, dt, active);
            return;
        }
        // Beside it and a little behind, in the way it's heading.
        double fx = Math.Sin(l.Angle), fy = Math.Cos(l.Angle);
        double tx = l.X + fy * a.OffsetX - fx * 22, ty = l.Y - fx * a.OffsetX - fy * 22;
        // Catch up to its place if it isn't there yet.
        double k = Math.Min(1, dt * 8);
        double nx = a.X + l.Vx * dt + (tx - a.X) * k, ny = a.Y + l.Vy * dt + (ty - a.Y) * k;
        (a.Vx, a.Vy) = ((nx - a.X) / dt, (ny - a.Y) / dt);
        (a.X, a.Y) = (nx, ny);
        a.Angle = l.Angle;
        if (active) Shoot(a, dt);
    }

    /// <summary>Off the bottom: round again from the top, back to its place (or, for the last few, straight back in).</summary>
    private void Wrap(Alien a, bool active)
    {
        a.Y = Field.Top - 40;
        a.T = 0;
        if (active && LastStand && !Calm)
        {
            a.State = AlienState.Diving;
            a.T = LoopSeconds;
            a.TargetX = Ship.X;
            (a.Vx, a.Vy) = (0, 1);
            a.Shots = 1 + _rng.Next(2);
            a.FireIn = 0.3 + _rng.NextDouble() * 0.5;
        }
        else a.State = AlienState.Returning;
        foreach (var e in a.Escorts.Where(e => !e.Dead && e.Leader == a))
        {
            e.Leader = null;
            e.State = AlienState.Returning;
            e.Y = a.Y - 24;
            e.T = 0;
        }
    }

    private void Return(Alien a, double dt)
    {
        double speed = Phase == Phase.Start && !_ready ? 340 : 170 * Pace;
        double tx = HomeX(a.Col) + Sway, ty = HomeY(a.Row);
        double dx = tx - a.X, dy = ty - a.Y, d = Math.Sqrt(dx * dx + dy * dy);
        if (d < speed * dt + 1)
        {
            (a.X, a.Y) = (tx, ty);
            a.State = AlienState.Holding;
            a.T = 0;
            a.Escorts.Clear();
            a.EscortsAtStart = 0;
        }
        else
        {
            (a.Vx, a.Vy) = (dx / d * speed, dy / d * speed);
            a.X += a.Vx * dt;
            a.Y += a.Vy * dt;
            a.Angle = 0;
        }
    }

    private void Shoot(Alien a, double dt)
    {
        if (Calm || a.Shots <= 0) return;
        a.FireIn -= dt;
        if (a.FireIn > 0 || a.Y < 220 || a.Y > Field.ShipY - 170) return;
        if (Bullets.Count >= MaxBullets) return;
        a.Shots--;
        a.FireIn = (0.25 + 0.3 * _rng.NextDouble()) / Pace;
        double vy = BulletSpeed * Pace * 0.9;
        double t = (Field.ShipY - a.Y) / vy;
        double vx = Math.Clamp((Ship.X - a.X) / t, -110, 110) + (_rng.NextDouble() - 0.5) * 30;
        Bullets.Add(new Bullet { X = a.X, Y = a.Y + 12, Vx = vx, Vy = vy });
        Play(Sfx.AlienFire);
    }

    /// <summary>Every so often, another one (or a commander and its escort) peels off the formation.</summary>
    private void Launch(double dt)
    {
        var holding = Aliens.Where(a => !a.Dead && a.State == AlienState.Holding).ToList();
        if (holding.Count == 0) return;
        int flying = Aliens.Count(a => !a.Dead && a.State is AlienState.Diving or AlienState.Escorting);
        // The last few come at you without a pause.
        if (LastStand)
        {
            if (flying < 3) StartDive(holding[_rng.Next(holding.Count)], escorted: false);
            return;
        }
        _diveIn -= dt;
        if (_diveIn > 0 || flying >= MaxDivers) return;
        _diveIn = Math.Max(0.5, 2.4 - 0.2 * (Wave - 1)) * (0.6 + 0.8 * _rng.NextDouble());
        _dives++;
        // Every fourth or so is a commander's, if there's one left.
        var commanders = holding.Where(a => a.Kind == AlienKind.Commander).ToList();
        if (commanders.Count > 0 && _dives % 4 == 0)
        {
            StartDive(commanders[_rng.Next(commanders.Count)], escorted: true);
            return;
        }
        // Otherwise one from the end of a row, on one side or the other.
        int side = _rng.Next(2) == 0 ? -1 : 1;
        var pool = holding.Where(a => a.Kind != AlienKind.Commander).ToList();
        if (pool.Count == 0) pool = holding;
        var edge = side < 0 ? pool.Min(a => a.Col) : pool.Max(a => a.Col);
        var near = pool.Where(a => Math.Abs(a.Col - edge) <= 1).ToList();
        StartDive(near[_rng.Next(near.Count)], escorted: false);
    }

    private void StartDive(Alien a, bool escorted)
    {
        a.State = AlienState.Diving;
        a.T = 0;
        a.Side = a.Col < Field.Columns / 2 ? -1 : 1;
        a.TargetX = Ship.X;
        a.Shots = a.Kind switch { AlienKind.Drone => 1, AlienKind.Stinger => 2, _ => 2 } + (Wave >= 4 ? 1 : 0);
        a.FireIn = 0.2 + _rng.NextDouble() * 0.4;
        a.Escorts.Clear();
        a.EscortsAtStart = 0;
        if (escorted)
        {
            foreach (var g in Aliens.Where(g => !g.Dead && g.Kind == AlienKind.Guard && g.State == AlienState.Holding && Math.Abs(g.Col - a.Col) <= 1)
                         .OrderBy(g => Math.Abs(g.Col - a.Col)).ThenBy(_ => _rng.Next()).Take(2))
            {
                g.State = AlienState.Escorting;
                g.Leader = a;
                g.T = 0;
                g.OffsetX = g.Col < a.Col ? -26 : g.Col > a.Col ? 26 : (a.Escorts.Count == 0 ? -26 : 26);
                g.Shots = 1;
                g.FireIn = 0.4 + _rng.NextDouble() * 0.5;
                a.Escorts.Add(g);
            }
            // Two on one side: spread them.
            if (a.Escorts.Count == 2 && a.Escorts[0].OffsetX == a.Escorts[1].OffsetX) a.Escorts[1].OffsetX = -a.Escorts[0].OffsetX;
            a.EscortsAtStart = a.Escorts.Count;
            if (a.Escorts.Count > 0) Play(Sfx.Escort);
        }
        Play(Sfx.Dive);
    }

    private void MoveBullets(double dt)
    {
        foreach (var b in Bullets)
        {
            b.X += b.Vx * dt;
            b.Y += b.Vy * dt;
            if (b.Y > Field.Bottom + 10) b.Dead = true;
        }
        Bullets.RemoveAll(b => b.Dead);
    }

    private void Hits()
    {
        var s = Ship;
        if (!s.Alive) return;
        foreach (var b in Bullets)
        {
            if (Math.Abs(b.X - s.X) >= 13 || Math.Abs(b.Y - Field.ShipY) >= 14) continue;
            b.Dead = true;
            Die("SHOT DOWN");
            return;
        }
        foreach (var a in Aliens)
        {
            if (a.Dead || a.State == AlienState.Holding) continue;
            if (Math.Abs(a.X - s.X) >= 24 || Math.Abs(a.Y - Field.ShipY) >= 20) continue;
            // Both go down.
            KillAlien(a);
            Die("RAMMED");
            return;
        }
    }

    private void Die(string cause)
    {
        Cause = cause;
        Ship.Alive = false;
        Lives--;
        Missile = null;
        Effects.Add(new Effect { Kind = EffectKind.Blast, X = Ship.X, Y = Field.ShipY, Life = 1.4, Size = 1 });
        SetPhase(Phase.Dying);
        Music(false);
        Play(Sfx.Death);
    }

    /// <summary>The next fighter, in the middle, once the swarm is back in its places.</summary>
    private void Respawn()
    {
        ResetShip();
        Bullets.Clear();
        Missile = null;
        foreach (var a in Aliens.Where(a => !a.Dead && a.State is AlienState.Diving or AlienState.Escorting))
        {
            a.State = AlienState.Returning;
            a.Leader = null;
            a.T = 0;
        }
        SetPhase(Phase.Start);
        _ready = true;
        _diveIn = 2;
        Say("READY", null, ReadySeconds);
    }

    private void Progress()
    {
        if (Remaining == 0 && !Calm)
        {
            Aliens.Clear();
            SetPhase(Phase.Clear);
            Bullets.Clear();
            Missile = null;
            Music(false);
            Say("SWARM DESTROYED", null, ClearSeconds);
            Play(Sfx.Clear);
        }
    }

    private void AddScore(int points)
    {
        Score += points;
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into the play of a wave, the swarm in its places.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        BeginWave(wave);
        foreach (var a in Aliens)
        {
            a.State = AlienState.Holding;
            a.X = HomeX(a.Col) + Sway;
            a.Y = HomeY(a.Row);
        }
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    internal void ClearAliens() => Aliens.Clear();

    internal Alien PutAlien(AlienKind kind, int col, int row)
    {
        var a = AddAlien(kind, col, row);
        a.X = HomeX(col) + Sway;
        return a;
    }

    internal void PutBullet(double x, double y, double vx = 0, double vy = BulletSpeed) => Bullets.Add(new Bullet { X = x, Y = y, Vx = vx, Vy = vy });

    internal void SendDive(Alien a, bool escorted = false) => StartDive(a, escorted);

    internal void SetShipX(double x) => Ship.X = x;
    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
}
