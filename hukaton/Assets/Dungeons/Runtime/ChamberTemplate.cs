using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Dungeons.Unity
{
    /// <summary>
    /// A hand-made chamber: sits on the dungeon grid with its cell (0,0) centred on the prefab origin at floor level,
    /// +Z = north. The planner reads its cells and doors; the builder lays the earth shell (walls, floor, vault) around
    /// it and places the chamber prefab itself for everything hand-placed inside (props, lights, spawn points).
    /// </summary>
    public sealed class ChamberTemplate : MonoBehaviour
    {
        [Serializable]
        public struct Door
        {
            public Vector2Int cell;
            public Dir side;
        }

        public string id = "chamber";
        public RoomRole role;
        [Tooltip("Cells of the chamber on the 4 m grid, in its own frame.")]
        public List<Vector2Int> cells = new List<Vector2Int> { Vector2Int.zero };
        [Tooltip("Where a corridor may join: a cell and the side it leaves through. Unused doors become walls.")]
        public List<Door> doors = new List<Door>();
        [Tooltip("Wall rows, 4 m each.")]
        [Range(1, 2)] public int rows = 1;
        [Tooltip("Entrance only: the cell and side of the way back to the surface.")]
        public bool hasWayOut;
        public Door wayOut;
        [Tooltip("Cells whose ceiling is a light slot (vent collar) instead of a vault.")]
        public List<Vector2Int> ventCells = new List<Vector2Int>();
        [Tooltip("Walls in front of which no niche or wall dressing may be laid (props stand there).")]
        public List<Door> keepClear = new List<Door>();

        public RoomShape ToShape()
        {
            var shape = new RoomShape { Id = id, Role = role, Rows = rows };
            foreach (var c in cells) shape.Cells.Add(new Cell(c.x, c.y));
            foreach (var d in doors) shape.Doors.Add((new Cell(d.cell.x, d.cell.y), d.side));
            if (hasWayOut) shape.WayOut = (new Cell(wayOut.cell.x, wayOut.cell.y), wayOut.side);
            return shape;
        }

        void OnDrawGizmos()
        {
            const float s = DungeonPlan.CellSize;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, .6f, .2f, .5f);
            foreach (var c in cells) Gizmos.DrawWireCube(new Vector3(c.x * s, rows * 2f, c.y * s), new Vector3(s, rows * 4f, s));
            Gizmos.color = Color.green;
            foreach (var d in doors) Gizmos.DrawSphere(new Vector3(d.cell.x * s, 1f, d.cell.y * s) + DungeonGeometry.Offset(d.side) * (s / 2), .4f);
            if (hasWayOut)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawSphere(new Vector3(wayOut.cell.x * s, 1f, wayOut.cell.y * s) + DungeonGeometry.Offset(wayOut.side) * (s / 2), .5f);
            }
        }
    }

    public static class DungeonGeometry
    {
        public static Vector3 Offset(Dir d) => d switch
        {
            Dir.North => Vector3.forward,
            Dir.East => Vector3.right,
            Dir.South => Vector3.back,
            _ => Vector3.left,
        };

        /// <summary>Yaw that turns +Z (north) to face <paramref name="d"/>.</summary>
        public static float Yaw(Dir d) => (int)d * 90f;

        public static Vector3 Center(Cell c, float floor) => new Vector3(c.X * DungeonPlan.CellSize, floor, c.Y * DungeonPlan.CellSize);
    }
}
