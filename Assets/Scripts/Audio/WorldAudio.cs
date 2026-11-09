using Networking;
using UnityEngine;
using World;
using World.Blocks;

namespace GameAudio
{
    /// <summary>
    /// Block break/place sounds for every change in the world (yours and
    /// other players'), and muffled hearing while your head is under water.
    /// Added automatically by the WorldManager on machines that render.
    /// </summary>
    public sealed class WorldAudio : MonoBehaviour
    {
        private const int MaxSoundsPerFrame = 4;
        private const float MaxDistance = 32f;

        private WorldManager _world;
        private int _soundsThisFrame;
        private AudioLowPassFilter _underwaterFilter;

        private void Awake()
        {
            _world = GetComponent<WorldManager>();

            if (_world != null)
                _world.BlockReplaced += OnBlockReplaced;
        }

        private void OnDestroy()
        {
            if (_world != null)
                _world.BlockReplaced -= OnBlockReplaced;
        }

        private void LateUpdate()
        {
            _soundsThisFrame = 0;
            UpdateUnderwaterFilter();
        }

        private void OnBlockReplaced(Vector3Int voxel, BlockId oldBlock, BlockId newBlock)
        {
            // Rate limit: a burst of edits (e.g. joining a server) stays quiet.
            if (_soundsThisFrame >= MaxSoundsPerFrame)
                return;

            Vector3 position = _world.VoxelCenter(voxel);
            Camera listener = Camera.main;

            if (listener == null || (listener.transform.position - position).sqrMagnitude > MaxDistance * MaxDistance)
                return;

            _soundsThisFrame++;

            bool broken = newBlock == BlockId.Air || BlockRegistry.IsLiquid(newBlock);
            GameSounds.PlayAt(broken ? GameSounds.Break(oldBlock) : GameSounds.Place(newBlock), position);
        }

        private void UpdateUnderwaterFilter()
        {
            Camera camera = Camera.main;

            if (camera == null)
                return;

            if (_underwaterFilter == null || _underwaterFilter.gameObject != camera.gameObject)
            {
                _underwaterFilter = camera.GetComponent<AudioLowPassFilter>();

                if (_underwaterFilter == null)
                    _underwaterFilter = camera.gameObject.AddComponent<AudioLowPassFilter>();
            }

            bool underwater = false;
            Items.PlayerInventory local = Items.PlayerInventory.Local;

            if (local != null && local.TryGetComponent(out CharacterMotor motor))
                underwater = motor.IsHeadUnderwater;

            _underwaterFilter.cutoffFrequency = Mathf.MoveTowards(
                _underwaterFilter.cutoffFrequency,
                underwater ? 700f : 22000f,
                Time.deltaTime * 60000f);
        }
    }
}
