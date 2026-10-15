using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAGalaxian.Engine;
using Effect = AVAGalaxian.Engine.Effect;

namespace AVAGalaxian.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAGalaxian/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Fire = 0xFFFF8A2A, Cyan = 0xFF6AE0FF;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        DrawBackdrop(dc);
        bool inGame = g.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;
        if (inGame)
        {
            using (dc.PushClip(new Rect(Field.Left, Field.Top - 4, Field.Right - Field.Left, Field.Bottom - Field.Top + 4)))
            {
                foreach (var a in g.Aliens.Where(a => !a.Dead)) DrawAlien(dc, a);
                if (g.Missile is { } m)
                {
                    dc.FillRectangle(Brush(Alpha(Yellow, 0.3)), new Rect(m.X - 3, m.Y - 2, 6, 18));
                    dc.FillRectangle(Brush(0xFFFFFFE0), new Rect(m.X - 1.5, m.Y, 3, 13));
                }
                foreach (var b in g.Bullets)
                {
                    dc.DrawEllipse(Brush(Alpha(0xFFFF6A4A, 0.3)), null, new Point(b.X, b.Y), 5, 8);
                    dc.DrawLine(Pen(0xFFFFE0E0, 3), new Point(b.X - b.Vx * 0.025, b.Y - 7), new Point(b.X, b.Y + 3));
                }
                if (g.Ship.Alive) DrawShip(dc, new Point(g.Ship.X, Field.ShipY), loaded: g.Missile == null, 1);
            }
            DrawPanels(dc);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 18), 26, Yellow, center: true);
    }

    /// <summary>Deep space, its many-coloured stars scrolling down past; the battle's column darker than the panels beside it.</summary>
    private void DrawBackdrop(DrawingContext dc)
    {
        dc.FillRectangle(Brush(0xFF080A1A), new Rect(0, 0, Field.Width, Field.Height));
        bool inGame = Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;
        if (inGame) dc.FillRectangle(Brush(0xFF000004), new Rect(Field.Left, Field.Top, Field.Right - Field.Left, Field.Bottom - Field.Top));
        double left = inGame ? Field.Left : 0, width = inGame ? Field.Right - Field.Left : Field.Width;
        uint[] colours = [0xFFFF5A5A, 0xFF5AFF8A, 0xFF5A8AFF, 0xFFFFE05A, 0xFFFF5AFF, 0xFF5AFFFF, 0xFFFFFFFF];
        for (int k = 0; k < 110; k++)
        {
            double x = left + Hash(k, 1, 7) % (uint)width;
            double y = (Hash(k, 3, 7) % 768 + _clock * 46) % 768;
            if (inGame && y < Field.Top) continue;
            // Each blinks on and off in its own time.
            if (Math.Sin(_clock * (1.5 + k % 4) + k * 1.7) < -0.3) continue;
            dc.FillRectangle(Brush(Alpha(colours[k % colours.Length], 0.85)), new Rect(x, y, 2, 2));
        }
        if (inGame)
        {
            dc.DrawLine(Pen(0xFF22305A, 2), new Point(Field.Left, Field.Top), new Point(Field.Left, Field.Bottom));
            dc.DrawLine(Pen(0xFF22305A, 2), new Point(Field.Right, Field.Top), new Point(Field.Right, Field.Bottom));
        }
    }

    // ───────────────────────────── the fighter ─────────────────────────────

    /// <summary>The fighter: a white dart with blue fins and red wingtips; when it's loaded, the missile sits on its nose.</summary>
    private void DrawShip(DrawingContext dc, Point at, bool loaded, double scale)
    {
        double k = scale;
        Point P(double x, double y) => new(at.X + x * k, at.Y + y * k);
        double flick = 4 + 3 * Math.Sin(_clock * 37);
        Poly(dc, 0xFFFF8A2A, null, 0, P(-4, 14), P(4, 14), P(0, 14 + flick));
        // Swept wings, red at the tips.
        Poly(dc, 0xFF2A6AE0, 0xFF0A2A7A, 1.2, P(-20, 13), P(-4, -4), P(4, -4), P(20, 13), P(8, 10), P(-8, 10));
        Poly(dc, 0xFFE02A3A, null, 0, P(-20, 13), P(-16, 7), P(-12, 12));
        Poly(dc, 0xFFE02A3A, null, 0, P(20, 13), P(16, 7), P(12, 12));
        // Body and canopy.
        Poly(dc, 0xFFF4F4FA, 0xFF707090, 1.1, P(0, -16), P(6, -2), P(5, 14), P(-5, 14), P(-6, -2));
        dc.DrawEllipse(Brush(0xFFE02A3A), null, P(0, 2), 2.5 * k, 4.5 * k);
        if (loaded) dc.FillRectangle(Brush(0xFFFFF4A0), new Rect(at.X - 1.5 * k, at.Y - 25 * k, 3 * k, 10 * k));
    }

    // ───────────────────────────── the swarm ─────────────────────────────

    private void DrawAlien(DrawingContext dc, Alien a)
    {
        // Turned to the way it's flying; in the formation, facing down at you.
        using (dc.PushTransform(Matrix.CreateRotation(-a.Angle) * Matrix.CreateTranslation(a.X, a.Y)))
            DrawAlienShape(dc, a.Kind, _clock * 6 + a.Phase, 1);
    }

    /// <summary>
    /// The four kinds, drawn about (0, 0) facing down: a drone, a stinger and a guard are winged bugs of
    /// one shape in blue, violet and red; a commander is a broad yellow flagship with a red crest.
    /// </summary>
    private static void DrawAlienShape(DrawingContext dc, AlienKind kind, double flapClock, double k)
    {
        Point P(double x, double y) => new(x * k, y * k);
        double flap = Math.Sin(flapClock);
        uint body = GameEngine.Colour(kind);
        if (kind == AlienKind.Commander)
        {
            double w = 15 + 2 * flap;
            Poly(dc, 0xFFE02A3A, 0xFF7A0A1A, 1, P(-w, -6), P(-6, -10), P(6, -10), P(w, -6), P(10, 4), P(-10, 4));
            Poly(dc, body, 0xFF8A6A0A, 1.2, P(-10, -8), P(-4, -12), P(4, -12), P(10, -8), P(8, 6), P(3, 12), P(-3, 12), P(-8, 6));
            // The crest, and its eyes.
            Poly(dc, 0xFFE02A3A, null, 0, P(-6, -12), P(-3, -17), P(0, -12), P(3, -17), P(6, -12));
            dc.DrawEllipse(Brush(0xFF2A1A6A), null, P(-4, 1), 2.2 * k, 2.2 * k);
            dc.DrawEllipse(Brush(0xFF2A1A6A), null, P(4, 1), 2.2 * k, 2.2 * k);
            return;
        }
        double wing = kind switch { AlienKind.Guard => 15, AlienKind.Stinger => 13, _ => 12 };
        double lift = 6 * flap;
        uint wingColour = kind switch { AlienKind.Guard => 0xFFFFA04A, AlienKind.Stinger => 0xFF6AE0FF, _ => 0xFFB0D0FF };
        // The wings, beating.
        Poly(dc, Alpha(wingColour, 0.95), null, 0, P(-3, -2), P(-wing, -6 - lift), P(-wing - 2, 4 - lift / 2), P(-4, 5));
        Poly(dc, Alpha(wingColour, 0.95), null, 0, P(3, -2), P(wing, -6 - lift), P(wing + 2, 4 - lift / 2), P(4, 5));
        // The body: a stinger has a tail-spike, a guard horns.
        Poly(dc, body, 0xFF101030, 1, P(0, -11), P(5, -5), P(5, 6), P(0, 11), P(-5, 6), P(-5, -5));
        if (kind == AlienKind.Stinger) Poly(dc, 0xFFFFFFFF, null, 0, P(-2, -10), P(0, -16), P(2, -10));
        if (kind == AlienKind.Guard)
        {
            dc.DrawLine(Pen(0xFFFFE04A, 1.6), P(-3, -9), P(-7, -15));
            dc.DrawLine(Pen(0xFFFFE04A, 1.6), P(3, -9), P(7, -15));
        }
        dc.DrawEllipse(Brush(0xFFFFFF60), null, P(-2.4, 4), 1.6 * k, 1.6 * k);
        dc.DrawEllipse(Brush(0xFFFFFF60), null, P(2.4, 4), 1.6 * k, 1.6 * k);
    }

    // ───────────────────────────── effects ─────────────────────────────

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Burst:
            {
                double r = (6 + 30 * t) * e.Size;
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, 1 - t), 2.5), c, r, r);
                for (int k = 0; k < 10; k++)
                {
                    double a = k * Math.PI / 5 + Hash(k, (int)e.X, 1) % 10 * 0.06, d = r * (0.6 + Hash(k, 2, 3) % 10 * 0.06);
                    dc.FillRectangle(Brush(Alpha(k % 2 == 0 ? 0xFFFFF0C0 : e.Colour, 1 - t)), new Rect(c.X + Math.Cos(a) * d - 2, c.Y + Math.Sin(a) * d - 2, 4, 4));
                }
                break;
            }
            case EffectKind.Blast:
            {
                double r = (16 + 70 * Math.Sqrt(t)) * e.Size;
                dc.DrawEllipse(Brush(Alpha(0xFFFFE080, (1 - t) * 0.8)), null, c, r * 0.6, r * 0.6);
                dc.DrawEllipse(null, Pen(Alpha(Fire, 1 - t), 6), c, r, r * 0.8);
                for (int k = 0; k < 18; k++)
                {
                    double a = k * Math.PI / 9 + Hash(k, (int)e.X, 5) % 10 * 0.05, d = r * (0.7 + Hash(k, 3, 5) % 10 * 0.05);
                    uint col = k % 3 == 0 ? 0xFFFFFFFF : k % 3 == 1 ? Fire : Yellow;
                    dc.FillRectangle(Brush(Alpha(col, 1 - t)), new Rect(c.X + Math.Cos(a) * d - 3, c.Y + Math.Sin(a) * d * 0.8 - 3, 6, 6));
                }
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 24), 26, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 40, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, panels and screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(16, 18), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 48), 36, Yellow);
        Text(dc, "HI", new Point(Field.Width - 16, 18), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 48), 30, Dim, right: true);
        if (g.State is GameState.Playing or GameState.Attract or GameState.Paused)
            Text(dc, $"WAVE {g.Wave}", new Point(Field.Width / 2, 36), 38, White, center: true);
    }

    /// <summary>The spare fighters and a flag for each wave on the left; what the swarm is worth on the right.</summary>
    private void DrawPanels(DrawingContext dc)
    {
        var g = Engine;
        double lx = Field.Left / 2, rx = Field.Right + (Field.Width - Field.Right) / 2;
        Text(dc, "FIGHTERS", new Point(lx, 120), 30, Dim, center: true);
        int spare = Math.Max(0, g.Lives - (g.Ship.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 9); i++)
            DrawShip(dc, new Point(lx - 48 + i % 3 * 48, 170 + i / 3 * 46), false, 0.9);

        Text(dc, "WAVES", new Point(lx, 340), 30, Dim, center: true);
        // A big flag for each ten, a small one for each of the rest.
        int big = g.Wave / 10, small = g.Wave % 10, n = 0;
        for (int i = 0; i < big + small && n < 15; i++, n++)
        {
            bool b = i < big;
            double x = lx - 60 + n % 5 * 30, y = 380 + n / 5 * 40;
            dc.DrawLine(Pen(0xFFC0C0D0, 2), new Point(x, y - (b ? 16 : 11)), new Point(x, y + 12));
            Poly(dc, b ? Yellow : Red, null, 0, new Point(x, y - (b ? 16 : 11)), new Point(x + (b ? 16 : 11), y - (b ? 10 : 7)), new Point(x, y - (b ? 4 : 3)));
        }
        if (g.LastStand && g.Phase == Phase.Play && (int)(_clock * 3) % 2 == 0)
            Text(dc, "LAST FEW!", new Point(lx, 560), 30, Red, center: true);

        Text(dc, "THE SWARM", new Point(rx, 120), 30, Dim, center: true);
        Text(dc, "HOLD  DIVE", new Point(rx + 22, 150), 22, Dim, center: true);
        (AlienKind Kind, string Hold, string Dive)[] rows =
        [
            (AlienKind.Commander, "60", "150+"),
            (AlienKind.Guard, "50", "100"),
            (AlienKind.Stinger, "40", "80"),
            (AlienKind.Drone, "30", "60"),
        ];
        for (int i = 0; i < rows.Length; i++)
        {
            double y = 190 + i * 46;
            using (dc.PushTransform(Matrix.CreateTranslation(rx - 60, y)))
                DrawAlienShape(dc, rows[i].Kind, _clock * 6 + i, 1);
            Text(dc, rows[i].Hold, new Point(rx - 2, y), 26, White, center: true);
            Text(dc, rows[i].Dive, new Point(rx + 54, y), 26, Yellow, center: true);
        }
        Text(dc, "COMMANDER", new Point(rx, 390), 24, 0xFFFFD84A, center: true);
        Text(dc, "WITH ESCORT", new Point(rx, 414), 22, Dim, center: true);
        Text(dc, "200 / 300", new Point(rx, 440), 24, White, center: true);
        Text(dc, "ESCORT SHOT", new Point(rx, 470), 22, Dim, center: true);
        Text(dc, "FIRST: 800", new Point(rx, 496), 26, Yellow, center: true);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = 470;
        var (w, h) = (Math.Min(Field.Right - Field.Left, Math.Max(msg.Length * 28, (g.SubMessage?.Length ?? 0) * 15) + 60), g.SubMessage != null ? 110.0 : 74.0);
        dc.FillRectangle(Brush(0xC0080A20), new Rect(cx - w / 2, cy - 48, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 12 ? 44 : 56, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 36), 28, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        Text(dc, "AVA GALAXIAN", new Point(cx, 108), 110, Cyan, center: true);
        Text(dc, "AFTER THE 1979 ARCADE CLASSIC", new Point(cx, 178), 28, Dim, center: true);
        // A commander and its escort, swooping round.
        double sweep = Math.Sin(_clock * 0.9) * 0.3;
        using (dc.PushTransform(Matrix.CreateRotation(sweep) * Matrix.CreateTranslation(cx, 258)))
        {
            using (dc.PushTransform(Matrix.CreateTranslation(-50, -34))) DrawAlienShape(dc, AlienKind.Guard, _clock * 6, 1.5);
            using (dc.PushTransform(Matrix.CreateTranslation(50, -34))) DrawAlienShape(dc, AlienKind.Guard, _clock * 6 + 1, 1.5);
            DrawAlienShape(dc, AlienKind.Commander, _clock * 6, 2);
        }
        string[] lines =
        [
            "THE SWARM SWAYS IN FORMATION - AND PEELS OFF TO DIVE AT YOU",
            "ONE MISSILE AT A TIME: MAKE IT COUNT",
            "SHOOT A COMMANDER'S ESCORTS FIRST FOR THE BIG BONUS",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx, 322 + i * 30), 25, White, center: true);
        double[] xs = [cx - 330, cx - 110, cx + 110, cx + 330];
        AlienKind[] kinds = [AlienKind.Drone, AlienKind.Stinger, AlienKind.Guard, AlienKind.Commander];
        string[] names = ["DRONE", "STINGER", "GUARD", "COMMANDER"];
        string[] hold = ["30", "40", "50", "60"], dive = ["DIVING 60", "DIVING 80", "DIVING 100", "DIVING 150-800"];
        for (int i = 0; i < 4; i++)
        {
            using (dc.PushTransform(Matrix.CreateTranslation(xs[i], 440))) DrawAlienShape(dc, kinds[i], _clock * 6 + i, 1.6);
            Text(dc, names[i], new Point(xs[i], 482), 26, GameEngine.Colour(kinds[i]), center: true);
            Text(dc, hold[i], new Point(xs[i], 508), 24, Yellow, center: true);
            Text(dc, dive[i], new Point(xs[i], 532), 22, Dim, center: true);
        }
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 600), 46, Yellow, center: true);
        Text(dc, "LEFT/RIGHT: FLY    SPACE: FIRE    P: PAUSE    M: SOUND", new Point(cx, 664), 26, Dim, center: true);
        Text(dc, $"EXTRA FIGHTER AT {GameEngine.FirstExtraLife} AND EVERY {GameEngine.ExtraLifeEvery}", new Point(cx, 700), 24, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "TOP PILOTS", new Point(cx, 130), 72, Cyan, center: true);
        Text(dc, "RANK NAME    SCORE   WAVE", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
