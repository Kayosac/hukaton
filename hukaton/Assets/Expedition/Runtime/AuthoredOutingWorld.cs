using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A ready-made location (the Old Apiary): the same place every outing, its finds, obstacles, props and creatures
    /// reset each time. It sits in the scene switched off and is switched on only for an outing that chose it.
    /// </summary>
    public sealed class AuthoredOutingWorld : MonoBehaviour, IOutingWorld
    {
        [Tooltip("Shown in the departure title and on the choice at the door.")]
        public string placeName = "Старая пасека";
        public Transform departure;
        [Tooltip("Filled by the setup from the children; the expedition searches these while the outing is here.")]
        public List<WorldLoot> finds = new List<WorldLoot>();
        public List<WorldObstacle> obstacles = new List<WorldObstacle>();
        public List<WorldCreature> creatures = new List<WorldCreature>();
        public List<PhysicsProp> props = new List<PhysicsProp>();

        ExpeditionWorld world;
        int outings;

        public Pose BeginOuting(ExpeditionWorld expedition)
        {
            EndOuting();
            world = expedition;
            gameObject.SetActive(true);
            Fill(world.finds, finds);
            Fill(world.obstacles, obstacles);
            Fill(world.creatures, creatures);
            Fill(world.props, props);
            var random = new System.Random(++outings * 1637 + expedition.workshop.Model.Journeys);
            foreach (var o in obstacles) if (o) { o.world = expedition; o.ResetForOuting(); }
            foreach (var f in finds) if (f) { f.world = expedition; f.ResetForOuting(random); }
            foreach (var p in props) if (p) p.ResetForOuting();
            foreach (var c in creatures) if (c) { c.world = expedition; c.ResetForOuting(random); }
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
            world = null;
            gameObject.SetActive(false);
        }

        static void Fill<T>(List<T> target, List<T> source) where T : Object
        {
            target.Clear();
            foreach (var item in source) if (item) target.Add(item);
        }
    }
}
