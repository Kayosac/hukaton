using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// Puts one chest's model on another: body and lid meshes and materials, the lid's hinge, the solid boxes and the
    /// mouth. All chest art prefabs (ChestArt, CH01–CH05) share one layout — five boxes on the root, a «Body» child and
    /// a «Lid» child with its own box — so this copies serialized values from the richer tier's prefab; nothing is built.
    /// Used by Gilding: a shut chest takes the look of the tier it was raised to.
    /// </summary>
    public static class ChestLook
    {
        public const string BodyName = "Body";

        /// <summary>Both chests have the shared layout, so <see cref="Copy"/> carries the whole look.</summary>
        public static bool Compatible(TreasureChest a, TreasureChest b)
        {
            if (!a || !b || !a.lid || !b.lid || !a.lid.Lid || !b.lid.Lid) return false;
            if (!Renders(a.transform.Find(BodyName)) || !Renders(b.transform.Find(BodyName))) return false;
            if (!Renders(a.lid.Lid) || !Renders(b.lid.Lid)) return false;
            return a.GetComponents<BoxCollider>().Length == b.GetComponents<BoxCollider>().Length
                   && a.lid.Lid.GetComponents<BoxCollider>().Length == b.lid.Lid.GetComponents<BoxCollider>().Length;
        }

        /// <summary>Gives <paramref name="to"/> (a chest in the world) the look of <paramref name="from"/> (a chest prefab).</summary>
        public static bool Copy(TreasureChest from, TreasureChest to)
        {
            if (!from || !to) return false;
            bool any = CopyPart(from.transform.Find(BodyName), to.transform.Find(BodyName));
            Transform fromLid = from.lid ? from.lid.Lid : null, toLid = to.lid ? to.lid.Lid : null;
            if (fromLid && toLid)
            {
                any |= CopyPart(fromLid, toLid);
                // The hinge sits elsewhere on a bigger chest; the lid is shut here, so its authored pose is its pose.
                toLid.localPosition = fromLid.localPosition;
                toLid.localRotation = fromLid.localRotation;
                CopyBoxes(fromLid, toLid);
            }
            CopyBoxes(from.transform, to.transform);
            if (from.mouth && to.mouth) to.mouth.localPosition = from.mouth.localPosition;
            if (any) to.name = from.name;
            return any;
        }

        static bool Renders(Transform part) => part && part.GetComponent<MeshFilter>() && part.GetComponent<MeshRenderer>();

        static bool CopyPart(Transform from, Transform to)
        {
            if (!from || !to) return false;
            var meshFrom = from.GetComponent<MeshFilter>();
            var meshTo = to.GetComponent<MeshFilter>();
            var rendererFrom = from.GetComponent<MeshRenderer>();
            var rendererTo = to.GetComponent<MeshRenderer>();
            if (!meshFrom || !meshTo || !rendererFrom || !rendererTo) return false;
            meshTo.sharedMesh = meshFrom.sharedMesh;
            rendererTo.sharedMaterials = rendererFrom.sharedMaterials;
            return true;
        }

        static void CopyBoxes(Transform from, Transform to)
        {
            var source = from.GetComponents<BoxCollider>();
            var target = to.GetComponents<BoxCollider>();
            for (int i = 0; i < target.Length; i++)
            {
                if (i >= source.Length) { target[i].enabled = false; continue; }
                target[i].center = source[i].center;
                target[i].size = source[i].size;
                target[i].enabled = source[i].enabled;
            }
        }
    }
}
