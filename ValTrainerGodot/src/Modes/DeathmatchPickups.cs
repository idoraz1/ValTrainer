using Godot;
using ValTrainer.Game;
using ValTrainer.Game.Bots;

namespace ValTrainer.Modes;

public sealed partial class DeathmatchMode
{
    /// <summary>VALORANT DM health pack: dropped where someone died, lasts 10 s, restores full HP + armour on touch.</summary>
    sealed class Pack
    {
        public Node3D Node = null!;
        public Vector3 Pos;
        public float Expires;
    }

    readonly List<Pack> packs = new();
    static StandardMaterial3D? packMat;

    void DropPack(Vector3 feet)
    {
        var floor = feet;
        floor.Y = Collision.Ground(feet + new Vector3(0, 0.3f, 0), G.Solid);
        packMat ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.35f, 1f, 0.55f),
        };
        var root = new Node3D { Name = "HealthPack" };
        var bar = new BoxMesh { Size = new Vector3(0.34f, 0.11f, 0.11f) };
        var a = new MeshInstance3D { Mesh = bar, MaterialOverride = packMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        var b = new MeshInstance3D { Mesh = bar, MaterialOverride = packMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, RotationDegrees = new Vector3(0, 0, 90) };
        root.AddChild(a); root.AddChild(b);
        G.World.AddChild(root);
        root.GlobalPosition = floor + new Vector3(0, 0.55f, 0);
        packs.Add(new Pack { Node = root, Pos = floor, Expires = Now + PackLifetime });
    }

    void UpdatePacks(float dt)
    {
        for (int i = packs.Count - 1; i >= 0; i--)
        {
            var p = packs[i];
            bool taken = false;
            if (PlayerAlive && (G.Player.Hp < 100 || G.Player.Shield < 50) && Near(PlayerFeet, p.Pos))
            {
                G.Player.Hp = 100; G.Player.Shield = 50;
                packsTaken++;
                G.Sound("tick", 0.9f, 0.6f);
                taken = true;
            }
            else
                foreach (var b in bots)
                    if (b.Alive && b.Body!.Hp < BotCharacter.MaxHp && Near(b.Body.Feet, p.Pos)) { b.Body.Hp = BotCharacter.MaxHp; taken = true; break; }
            if (taken || Now >= p.Expires)
            {
                G.Despawn(p.Node);
                packs.RemoveAt(i);
                continue;
            }
            if (GodotObject.IsInstanceValid(p.Node))
            {
                p.Node.RotateY(dt * 2.4f);
                p.Node.Position = p.Pos + new Vector3(0, 0.55f + 0.08f * Mathf.Sin(Now * 3f), 0);
                p.Node.Visible = p.Expires - Now > 2f || Mathf.PosMod(Now, 0.3f) < 0.18f; // blinks before it disappears
            }
        }
    }

    static bool Near(Vector3 feet, Vector3 pack) =>
        new Vector2(feet.X - pack.X, feet.Z - pack.Z).Length() < 1.1f && Mathf.Abs(feet.Y - pack.Y) < 1.2f;

    Vector3? NearestPack(Vector3 from, float maxDist)
    {
        Vector3? best = null; float bd = maxDist;
        foreach (var p in packs)
        {
            if (p.Expires - Now < 2.5f) continue;
            float d = p.Pos.DistanceTo(from);
            if (d < bd) { bd = d; best = p.Pos; }
        }
        return best;
    }
}
