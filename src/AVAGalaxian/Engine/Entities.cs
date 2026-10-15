namespace AVAGalaxian.Engine;

/// <summary>Left and right, and the fire button.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false);

/// <summary>
/// The screen, in pixels: the battle is fought in the column between <see cref="Left"/> and <see cref="Right"/>,
/// with the fighter sliding along the bottom at <see cref="ShipY"/>.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Left = 192, Right = 832, Top = 70, Bottom = 768;
    public const double CentreX = (Left + Right) / 2;
    public const double ShipY = 716;
    /// <summary>The formation: ten columns, six rows, and the gaps between.</summary>
    public const int Columns = 10, Rows = 6;
    public const double ColGap = 46, RowGap = 36, FormationTop = 118;
}

public sealed class Ship
{
    public double X = Field.CentreX;
    public bool Alive = true;
    public double Safe;
}

/// <summary>The fighter's missile: only one in the air at a time.</summary>
public sealed class Missile
{
    public double X, Y;
    public bool Dead;
}

public sealed class Bullet
{
    public double X, Y, Vx, Vy;
    public bool Dead;
}

public enum AlienKind
{
    /// <summary>The blue swarm: three rows of ten.</summary>
    Drone,
    /// <summary>A row of eight.</summary>
    Stinger,
    /// <summary>A row of six, who fly with the commanders.</summary>
    Guard,
    /// <summary>The two at the top: they lead dives with guards for escorts.</summary>
    Commander,
}

public enum AlienState
{
    /// <summary>In its place in the formation, flapping.</summary>
    Holding,
    /// <summary>Peeling off and diving at the fighter.</summary>
    Diving,
    /// <summary>Flying with a commander, in its escort.</summary>
    Escorting,
    /// <summary>Back down from the top of the screen to its place.</summary>
    Returning,
}

public sealed class Alien
{
    public AlienKind Kind;
    public int Col, Row;
    public AlienState State = AlienState.Holding;
    public double X, Y, Vx, Vy, Angle, T, Phase;
    public bool Dead;
    /// <summary>Which way it peeled off (-1 left, 1 right), and how many shots it has left this dive.</summary>
    public int Side = 1, Shots;
    public double FireIn, TargetX;
    /// <summary>For a commander: the guards flying with it, and how many it set off with.</summary>
    public readonly List<Alien> Escorts = [];
    public int EscortsAtStart;
    /// <summary>For an escort: its commander, and its place beside it.</summary>
    public Alien? Leader;
    public double OffsetX;
}

public enum EffectKind { Score, Burst, Word, Blast }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.7, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
