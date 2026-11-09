namespace World.Generation.Structures
{
    /// <summary>
    /// A generation pass that places multi-block objects: trees,
    /// boulders, and later dungeons, ruins, villages and so on.
    ///
    /// Structures can cross chunk borders. To stay deterministic without
    /// cross-chunk dependencies, every chunk asks each generator to place
    /// *all* structures that could reach into it - including ones rooted
    /// in neighbouring chunks - and writes only the blocks that land
    /// inside itself. So placement decisions must be a pure function of
    /// world position and seed (see <see cref="SurfaceFeatureGenerator"/>
    /// for the pattern).
    ///
    /// Implementations must be immutable and thread-safe; per-chunk state
    /// goes in <see cref="ChunkGenerationContext"/>.
    /// </summary>
    public interface IStructureGenerator
    {
        /// <summary>
        /// How far (in blocks) a structure can extend horizontally from
        /// its origin. Chunks look this far into their neighbours.
        /// </summary>
        int MaxHorizontalReach { get; }

        void Generate(ChunkGenerationContext context);
    }
}
