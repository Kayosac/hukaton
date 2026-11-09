using System.Linq;
using System.Text;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions;
using Hakaton.Expeditions.Cards;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

// TEMPORARY Play check of the awakened tree's look. Deleted after use.
static class TreeLookPlay
{
    static TreeAlly ally;
    static WorldCreature dog;
    static Vector3 forward;

    static ExpeditionWorld World => Object.FindAnyObjectByType<ExpeditionWorld>();

    public static string Depart()
    {
        var game = Object.FindAnyObjectByType<Hakaton.Game.GameLoop>();
        if (!game) return "no game";
        game.persistProgress = false;
        bool ok = game.TryDepart();
        return "persist=" + game.persistProgress + " depart=" + ok + " running=" + (World && World.Running);
    }

    public static string Wake()
    {
        var world = World;
        if (!world || !world.Running) return "not running";
        var player = world.motor.transform.position;
        var trees = Object.FindObjectsByType<AwakenableTree>(FindObjectsSortMode.None)
            .Where(t => t.Standing && NavMesh.SamplePosition(t.FootPosition, out _, 2, NavMesh.AllAreas))
            .OrderBy(t => (t.FootPosition - player).sqrMagnitude).ToList();
        AwakenableTree tree = null;
        Vector3 stand = Vector3.zero;
        foreach (var t in trees)
        {
            // A free walkable spot 7 m from the tree, toward the player.
            Vector3 dir = player - t.FootPosition; dir.y = 0;
            if (dir.sqrMagnitude < .01f) dir = Vector3.forward;
            for (int a = 0; a < 8 && !tree; a++)
            {
                Vector3 d = Quaternion.Euler(0, a * 45, 0) * dir.normalized;
                if (NavMesh.SamplePosition(t.FootPosition + d * 7, out var hit, 1, NavMesh.AllAreas)
                    && NavMesh.SamplePosition(t.FootPosition - d * 6, out _, 1.5f, NavMesh.AllAreas)
                    && (!Physics.Linecast(hit.position + Vector3.up * 1.6f, t.FootPosition + Vector3.up * 1.8f, out var block, ~0, QueryTriggerInteraction.Ignore) || t.Owns(block.collider)))
                { tree = t; stand = hit.position; }
            }
            if (tree) break;
        }
        if (!tree) tree = trees.FirstOrDefault();
        if (!tree) return "no tree";
        if (stand == Vector3.zero) stand = tree.FootPosition + Vector3.back * 7;
        Vector3 look = tree.FootPosition + Vector3.up * 1.6f - (stand + Vector3.up * 1.6f);
        forward = new Vector3(look.x, 0, look.z).normalized;
        world.motor.Teleport(stand + Vector3.up * .1f, Quaternion.LookRotation(look));
        var effect = AssetDatabase.LoadAssetAtPath<TreeAwakeningEffect>("Assets/Game/Data/Effects/Tree awakening.asset");
        var definition = AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Game/Data/tree_awakening.asset");
        var collider = tree.GetComponentsInChildren<Collider>().FirstOrDefault(c => !c.isTrigger) ?? tree.GetComponentInChildren<Collider>();
        Vector3 point = collider.ClosestPoint(stand + Vector3.up * 1.5f);
        effect.Apply(new ImpactContext(world, null, definition, collider, point, -forward, forward * 12, stand + Vector3.up * 1.5f));
        ally = Object.FindObjectsByType<TreeAlly>(FindObjectsSortMode.None).FirstOrDefault(x => x.Origin == tree);
        return "tree=" + tree.name + " at " + tree.FootPosition + " player=" + stand + " ally=" + (ally ? ally.name : "null") + " | " + Report();
    }

    /// <summary>The player walks off sideways: the tree follows.</summary>
    public static string Walk(string metres)
    {
        var world = World;
        float m = float.Parse(metres, System.Globalization.CultureInfo.InvariantCulture);
        Vector3 side = Vector3.Cross(Vector3.up, forward);
        Vector3 p = world.motor.transform.position + side * m;
        if (NavMesh.SamplePosition(p, out var hit, 3, NavMesh.AllAreas)) p = hit.position;
        Vector3 look = ally.Position + Vector3.up * 1.6f - (p + Vector3.up * 1.6f);
        world.motor.Teleport(p + Vector3.up * .1f, Quaternion.LookRotation(look));
        return Report();
    }

    public static string Dog()
    {
        var world = World;
        dog = world.creatures.Where(c => c && c.Alive && c.displayName != null && c.name.ToLowerInvariant().Contains("goblin dog"))
            .OrderBy(c => (c.body.position - ally.Position).sqrMagnitude).FirstOrDefault();
        if (!dog) dog = world.creatures.Where(c => c && c.Alive && c.temperament != 0).OrderBy(c => (c.body.position - ally.Position).sqrMagnitude).FirstOrDefault();
        if (!dog) return "no creature; names: " + string.Join(",", world.creatures.Where(c => c).Select(c => c.name));
        Vector3 p = ally.Position + forward * 6;
        if (NavMesh.SamplePosition(p, out var hit, 3, NavMesh.AllAreas)) p = hit.position;
        if (!dog.gameObject.activeSelf) dog.gameObject.SetActive(true);
        dog.body.position = p; dog.transform.position = p;
        if (dog.navigator && dog.navigator.enabled && dog.navigator.isOnNavMesh) dog.navigator.Warp(p);
        dog.TakeDamage(new DamageInfo(1, Vector3.zero, dog.Centre, ally.Position, DamageKind.Hit, ally));
        return "dog=" + dog.name + " hp=" + dog.Health + " | " + Report();
    }

    public static string Hurt()
    {
        ally.TakeDamage(new DamageInfo(1, Vector3.zero, ally.Centre, null, DamageKind.Hit, dog));
        return Report();
    }

    public static string Kill()
    {
        ally.TakeDamage(new DamageInfo(99, Vector3.zero, ally.Centre, null, DamageKind.Hit, dog));
        return Report();
    }

    /// <summary>The camera m metres from the tree on the player's side (or behind it from its foe), looking at it.</summary>
    public static string Frame(string arg)
    {
        var world = World;
        var parts = arg.Split(' ');
        float m = float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
        bool behind = parts.Length > 1 && dog;
        Vector3 from = behind ? ally.Position - (dog.body.position - ally.Position) : world.motor.transform.position;
        Vector3 d = from - ally.Position; d.y = 0;
        if (d.sqrMagnitude < .01f) d = -forward;
        Vector3 p = ally.Position + d.normalized * m;
        if (NavMesh.SamplePosition(p, out var hit, 3, NavMesh.AllAreas)) p = hit.position;
        Vector3 look = ally.Position + Vector3.up * 1.4f - (p + Vector3.up * 1.6f);
        world.motor.Teleport(p + Vector3.up * .1f, Quaternion.LookRotation(look));
        return Report();
    }

    public static string Report()
    {
        if (!ally) return "no ally";
        var look = ally.look as SpriteTreeAllyLook;
        var view = look ? look.view : null;
        var b = new StringBuilder();
        b.Append("ally ").Append(ally.State).Append(" phase=").Append(ally.AttackPhase).Append(" hp=").Append(ally.Health)
            .Append(" active=").Append(ally.gameObject.activeSelf)
            .Append(" pos=").Append(ally.Position).Append(" foe=").Append(ally.Foe ? ally.Foe.name : "-");
        if (view) b.Append(" drawing=").Append(view.Pose).Append(" back=").Append(view.ShowingBack)
            .Append(" sprite=").Append(view.spriteRenderer.sprite ? view.spriteRenderer.sprite.name : "-")
            .Append(" flip=").Append(view.spriteRenderer.flipX).Append(" a=").Append(view.spriteRenderer.color.a.ToString("0.00"))
            .Append(" off=").Append(view.transform.localPosition);
        if (dog) b.Append(" | dog ").Append(dog.State).Append(" hp=").Append(dog.Health).Append(" d=")
            .Append(Vector3.Distance(dog.body.position, ally.Position).ToString("0.0"));
        var world = World;
        if (world) b.Append(" | playerHP=").Append(world.vitals ? world.vitals.ToString() : "-");
        return b.ToString();
    }
}
