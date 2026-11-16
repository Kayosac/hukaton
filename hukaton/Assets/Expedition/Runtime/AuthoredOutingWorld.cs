using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A ready-made location (the Old Apiary): the same place every outing, its finds, obstacles, props and creatures
    /// reset each time. It sits in the scene switched off and is switched on only for an outing that chose it.
    /// </summary>
    public sealed class AuthoredOutingWorld : MonoBehaviour, IOutingWorld, IOutingLocation
    {
        [Tooltip("Shown in the departure title and on the choice at the door.")]
        public string placeName = "Старая пасека";
        public Transform departure;
        [Tooltip("Filled by the setup from the children; the expedition searches these while the outing is here.")]
        public List<WorldLoot> finds = new List<WorldLoot>();
        public List<WorldObstacle> obstacles = new List<WorldObstacle>();
        public List<WorldCreature> creatures = new List<WorldCreature>();
        public List<PhysicsProp> props = new List<PhysicsProp>();
        [Tooltip("Fixed things that break (fences): whole again every outing. Filled from the children when empty.")]
        public List<Breakable> breakables = new List<Breakable>();
        [Tooltip("Places where a chest may turn up; each rolls its table every outing.")]
        public List<ChestSite> chestSites = new List<ChestSite>();

        ExpeditionWorld world;
        int outings;

        /// <summary>For the cheat menu: a ready-made location has no rings or chunks, only where you are in it.</summary>
        public string DescribeLocation(Vector3 position)
        {
            var o = transform.position;
            float dx = position.x - o.x, dz = position.z - o.z;
            return "Готовая локация: " + placeName + "\nОт её центра: " + Mathf.Sqrt(dx * dx + dz * dz).ToString("0") + " м"
                + "\nПозиция: " + position.x.ToString("0") + ", " + position.y.ToString("0") + ", " + position.z.ToString("0");
        }

        public Pose BeginOuting(ExpeditionWorld expedition)
        {
            EndOuting();
            world = expedition;
            gameObject.SetActive(true);
            Gather();
            Fill(world.finds, finds);
            Fill(world.obstacles, obstacles);
            Fill(world.creatures, creatures);
            Fill(world.props, props);
            var random = new System.Random(++outings * 1637 + expedition.workshop.Model.Journeys);
            foreach (var o in obstacles) if (o) { o.world = expedition; o.ResetForOuting(); }
            // Props and fences stand back in place first: a wandering find looks for a free patch of ground among them.
            foreach (var p in props) if (p) p.ResetForOuting();
            foreach (var b in breakables) if (b) b.Restore();
            foreach (var f in finds) if (f) { f.world = expedition; f.ResetForOuting(random); }
            foreach (var c in creatures) if (c) { c.world = expedition; c.ResetForOuting(random); }
            foreach (var site in chestSites) if (site) site.ResetForOuting(expedition, random);
            var start = departure ? departure : transform;
            return new Pose(start.position, start.rotation);
        }

        public void EndOuting()
        {
            if (world)
            {
                world.finds.Clear();
                world.obstacles.Clear();
                world.creatures.Clear();
                world.props.Clear();
            }
            foreach (var site in chestSites) if (site) site.Clear();
            world = null;
            gameObject.SetActive(false);
        }

        bool gathered;

        /// <summary>
        /// Every loose thing and fixed breakable under the place is reset each outing, the ones made physical after the
        /// setup filled the lists too (PlaceLooseThings).
        /// </summary>
        void Gather()
        {
            if (gathered) return;
            gathered = true;
            var known = new HashSet<PhysicsProp>(props);
            foreach (var p in GetComponentsInChildren<PhysicsProp>(true)) if (known.Add(p)) props.Add(p);
            var fixedOnes = new HashSet<Breakable>(breakables);
            foreach (var b in GetComponentsInChildren<Breakable>(true))
                if (!b.TryGetComponent<PhysicsProp>(out _) && fixedOnes.Add(b)) breakables.Add(b);
        }

        static void Fill<T>(List<T> target, List<T> source) where T : Object
        {
            target.Clear();
            foreach (var item in source) if (item) target.Add(item);
        }

        /// <summary>Selected, shows the location's threat bands (kept on the departure point) around the start.</summary>
        void OnDrawGizmosSelected()
        {
            var bands = departure ? departure.GetComponent<OutingThreatBands>() : null;
            if (bands) bands.DrawGizmos();
        }
    }
}
