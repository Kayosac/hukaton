# Ice on water: editor steps still to run

The Unity editor for this worktree was not up while the code was written. To finish the wiring:

1. Copy `IceWaterAuthoring.cs.txt` to `hukaton/Assets/Editor/IceWaterAuthoring.cs`, let it compile.
2. Bridge: `invoke IceWaterAuthoring.Run`. It creates `Assets/Expedition/Prefabs/Ice/Ice block.prefab` and
   `Ice field.prefab`, places an Ice field instance next to every ExpeditionWorld (Hakaton, BeastBrawlLab,
   MimicLab, OddityLab, Debug Session prefab) with `world.ice` / `field.world` set, and sets the cave's flooded
   stretch `minimumIceRadius = 6.5` so the tutorial's one Ice card makes it crossable (the stretch is about 10 x 8 m).
   Check the log for "WARNING world inside prefab instance".
3. Delete the script and its .meta, run EditMode tests `IceCellsTests`, then play: throw Ice at a river crossing two
   chunks (one crooked slab across both), walk on it, throw Fire at it (it thaws, you fall in), reload a chunk.
