namespace World.Blocks
{
    /// <summary>
    /// Every block type in the world.
    ///
    /// The numeric values are stored in chunk data and will be
    /// written to save files and network messages, so never
    /// renumber existing entries. Only append new ones.
    /// </summary>
    public enum BlockId : byte
    {
        Air = 0,
        Grass = 1,
        Dirt = 2,
        Stone = 3,

        Bedrock = 4,
        Water = 5,
        Sand = 6,
        Sandstone = 7,
        Gravel = 8,
        Clay = 9,
        Snow = 10,
        Ice = 11,

        Log = 12,
        Leaves = 13,
        PineLog = 14,
        PineLeaves = 15,
        Cactus = 16,

        CoalOre = 17,
        CopperOre = 18,
        IronOre = 19,
        GoldOre = 20,
        CrystalOre = 21,

        MossyStone = 22,

        AutumnLeaves = 23,
        RedLeaves = 24,

        // Small decorations: not solid, drawn as little voxel tufts.
        TallGrass = 25,
        FlowerRed = 26,
        FlowerYellow = 27,
        FlowerBlue = 28,
        FlowerWhite = 29,
        DryGrass = 30
    }
}
