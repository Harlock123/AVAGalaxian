namespace AVAGalaxian.Engine;

/// <summary>
/// The computer pilot, for the demonstration (and the tests). It looks a moment ahead for bullets and
/// diving aliens coming its way and slips aside from them. Otherwise it lines up under the best target (a
/// diver first, a commander's escort before the commander, then the lowest and nearest in the formation),
/// allowing for where the target will be when the missile gets there, and fires when it's lined up.
/// </summary>
public sealed class ShipPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    // The missile goes on a fresh press, so it lets go of the button between shots.
    private bool _held;

    public Controls Decide(GameEngine g)
    {
        var c = Think(g);
        if (c.Fire && _held) c = c with { Fire = false };
        _held = c.Fire;
        return c;
    }

    /// <summary>How soon something will hit the fighter if it sits at <paramref name="x"/> (a large number for nothing soon).</summary>
    private static double Danger(GameEngine g, double x)
    {
        double soonest = 99;
        foreach (var b in g.Bullets)
        {
            if (b.Vy <= 0) continue;
            double t = (Field.ShipY - b.Y) / b.Vy;
            if (t < -0.06 || t > 1.3) continue;
            if (Math.Abs(b.X + b.Vx * Math.Max(0, t) - x) < 22) soonest = Math.Min(soonest, Math.Max(0, t));
        }
        foreach (var a in g.Aliens)
        {
            if (a.Dead || a.State is AlienState.Holding or AlienState.Returning) continue;
            for (double t = 0; t <= 0.9; t += 0.05)
            {
                double ax = a.X + a.Vx * t, ay = a.Y + a.Vy * t;
                if (Math.Abs(ay - Field.ShipY) < 30 && Math.Abs(ax - x) < 38) { soonest = Math.Min(soonest, t); break; }
            }
        }
        return soonest;
    }

    private Controls Think(GameEngine g)
    {
        var s = g.Ship;
        if (!s.Alive || g.Phase is not (Phase.Play or Phase.Start)) return default;
        double now = Danger(g, s.X);
        if (now < 0.75)
        {
            // Somewhere safe it can get to in time, as near as possible.
            double best = double.MaxValue, goal = s.X;
            for (double dx = -260; dx <= 260; dx += 10)
            {
                double x = s.X + dx;
                if (x < Field.Left + 20 || x > Field.Right - 20) continue;
                double time = Math.Abs(dx) / GameEngine.ShipSpeed;
                if (time > now - 0.03 && dx != 0) continue;
                if (Danger(g, x) < 1.0) continue;
                bool clear = true;
                for (double k = 10; k < Math.Abs(dx) && clear; k += 10)
                    if (Danger(g, s.X + Math.Sign(dx) * k) < k / GameEngine.ShipSpeed + 0.05) clear = false;
                if (clear && Math.Abs(dx) < best) { best = Math.Abs(dx); goal = x; }
            }
            if (best < double.MaxValue && Math.Abs(goal - s.X) > 2)
            {
                Doing = "dodge";
                return new Controls(Math.Sign(goal - s.X), 0, Fire: g.Missile == null);
            }
        }

        Alien? target = null;
        double cost = double.MaxValue, aim = s.X;
        foreach (var a in g.Aliens)
        {
            if (a.Dead || a.Y > Field.ShipY - 60 || a.Y < Field.Top) continue;
            double tShot = Math.Max(0, (Field.ShipY - 20 - a.Y) / GameEngine.MissileSpeed);
            double px = a.X + a.Vx * tShot;
            bool flying = a.State is AlienState.Diving or AlienState.Escorting;
            double c = Math.Abs(px - s.X) / GameEngine.ShipSpeed - (flying ? 0.7 : 0) - a.Y / 1500
                       - (a.State == AlienState.Escorting ? 0.3 : 0);
            if (c < cost) { cost = c; target = a; aim = px; }
        }
        Doing = target == null ? "wait" : target.State == AlienState.Holding ? "target" : "diver";
        aim = Math.Clamp(aim, Field.Left + 20, Field.Right - 20);
        int dir = Math.Abs(aim - s.X) > 4 ? Math.Sign(aim - s.X) : 0;
        // Don't walk into trouble on the way.
        if (dir != 0 && Danger(g, s.X + dir * 20) < 0.6) dir = 0;
        bool fire = g.Missile == null && Math.Abs(aim - s.X) < 9;
        return new Controls(dir, 0, fire);
    }
}
