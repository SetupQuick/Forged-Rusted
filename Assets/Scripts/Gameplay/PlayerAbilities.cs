using Items;
using Networking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Gameplay
{
    /// <summary>
    /// The local player's skill keys and what they show:
    ///   R - Fireball (spell, aimed with the camera)
    ///   V - Invisibility
    ///   F - Dash, G - Grappling hook, Space in the air - double jump
    ///       (those three run in the movement code, see CharacterMotor)
    /// Keeps the motor's unlocked movement abilities in sync with the
    /// server's status, draws the fireball and the grappling rope, and
    /// explains locked abilities instead of silently ignoring the key.
    /// Added to the local player by PlayerInventory.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAbilities : MonoBehaviour
    {
        private PlayerInventory _inventory;
        private CharacterMotor _motor;
        private LineRenderer _rope;

        public static PlayerStatus Status
        {
            get
            {
                ClientGameplayModule client = NetworkRuntime.Current != null ? NetworkRuntime.Current.ClientGameplay : null;
                return client != null && client.HasStatus ? client.Status : DefaultStatus;
            }
        }

        public static bool HasStatus
        {
            get
            {
                ClientGameplayModule client = NetworkRuntime.Current != null ? NetworkRuntime.Current.ClientGameplay : null;
                return client != null && client.HasStatus;
            }
        }

        private static readonly PlayerStatus DefaultStatus = new PlayerStatus { Health = 20f, MaxHealth = 20f, Level = 1, ExperienceToNext = 50 };

        private void Awake()
        {
            _inventory = GetComponent<PlayerInventory>();
            _motor = GetComponent<CharacterMotor>();
        }

        private void Update()
        {
            PlayerStatus status = Status;

            if (_motor != null)
            {
                var flags = CharacterMotor.MovementAbilities.None;
                if (status.HasSkill(Abilities.Dash)) flags |= CharacterMotor.MovementAbilities.Dash;
                if (status.HasSkill(Abilities.DoubleJump)) flags |= CharacterMotor.MovementAbilities.DoubleJump;
                if (status.HasSkill(Abilities.Grapple)) flags |= CharacterMotor.MovementAbilities.Grapple;
                _motor.Abilities = flags;
            }

            UpdateRope();

            if (PlayerInputReader.GameplayInputBlocked || status.Dead)
                return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.rKey.wasPressedThisFrame)
                TryCast(status, Abilities.Fireball, AbilityCode.Fireball, PlayerProgress.FireballCost);

            if (keyboard.vKey.wasPressedThisFrame)
                TryCast(status, Abilities.Invisibility, AbilityCode.Invisibility, PlayerProgress.InvisibilityCost);

            if (keyboard.fKey.wasPressedThisFrame && !status.HasSkill(Abilities.Dash))
                Explain("Dash");
            if (keyboard.gKey.wasPressedThisFrame && !status.HasSkill(Abilities.Grapple))
                Explain("Grappling Hook");
        }

        private void TryCast(PlayerStatus status, string skill, AbilityCode code, float manaCost)
        {
            if (!status.HasSkill(skill))
            {
                Explain(skill == Abilities.Fireball ? "Fireball" : "Invisibility");
                return;
            }

            if (status.Mana < manaCost)
            {
                GameUI.GameHud.Instance?.ShowMessage("Not enough mana");
                return;
            }

            Camera camera = Camera.main;
            Vector3 direction = camera != null ? camera.transform.forward : transform.forward;
            _inventory.UseAbility(code, direction);

            if (code == AbilityCode.Fireball && camera != null)
                FireballEffect.Launch(camera.transform.position + direction * 0.6f - camera.transform.up * 0.15f, direction);
        }

        private static void Explain(string ability)
        {
            GameUI.GameHud.Instance?.ShowMessage(
                string.IsNullOrEmpty(Status.ClassId)
                    ? "Choose a class first (K)"
                    : $"Unlock {ability} in the skill tree (K)");
        }

        /// <summary>A line from the hand to the hook point while grappling.</summary>
        private void UpdateRope()
        {
            bool show = _motor != null && _motor.IsGrappling;

            if (!show)
            {
                if (_rope != null)
                    _rope.enabled = false;
                return;
            }

            if (_rope == null)
            {
                var go = new GameObject("Grappling Rope");
                _rope = go.AddComponent<LineRenderer>();
                _rope.positionCount = 2;
                _rope.startWidth = _rope.endWidth = 0.03f;
                _rope.material = EffectMaterial(new Color(0.45f, 0.32f, 0.2f, 1f));
                _rope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            Camera camera = Camera.main;
            Vector3 hand = camera != null
                ? camera.transform.position + camera.transform.right * 0.25f - camera.transform.up * 0.25f
                : transform.position + Vector3.up;

            _rope.enabled = true;
            _rope.SetPosition(0, hand);
            _rope.SetPosition(1, _motor.GrapplePoint);
        }

        private void OnDestroy()
        {
            if (_rope != null)
                Destroy(_rope.gameObject);
        }

        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>
        /// A flat colour material from our own highlight shader (always in
        /// the build, unlike shaders looked up by name).
        /// </summary>
        public static Material EffectMaterial(Color color)
        {
            Material source = Resources.Load<Material>("World/HighlightMaterial");
            var material = source != null ? new Material(source) : new Material(Shader.Find("Hidden/Internal-Colored"));
            material.SetColor(ColorId, color);
            return material;
        }
    }

    /// <summary>A glowing ball flying along the aim (visual only; the server decides the hit).</summary>
    public sealed class FireballEffect : MonoBehaviour
    {
        private Vector3 _velocity;
        private float _life;

        public static void Launch(Vector3 position, Vector3 direction)
        {
            GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Fireball";
            Destroy(ball.GetComponent<Collider>());
            ball.transform.position = position;
            ball.transform.localScale = Vector3.one * 0.3f;

            Renderer renderer = ball.GetComponent<Renderer>();
            renderer.material = PlayerAbilities.EffectMaterial(new Color(1f, 0.55f, 0.15f, 0.95f));
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var light = ball.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.6f, 0.2f);
            light.range = 6f;
            light.intensity = 2f;

            var effect = ball.AddComponent<FireballEffect>();
            effect._velocity = direction.normalized * 30f;
            effect._life = PlayerProgress.FireballRange / 30f;
        }

        private void Update()
        {
            transform.position += _velocity * Time.deltaTime;
            _life -= Time.deltaTime;

            // Stop at the first solid block.
            World.WorldManager world = World.WorldManager.Active;
            if (world != null && world.TryGetLoadedBlockAt(transform.position, out World.Blocks.BlockId block) &&
                World.Blocks.BlockRegistry.IsSolid(block))
            {
                _life = 0f;
            }

            if (_life <= 0f)
                Destroy(gameObject);
        }
    }
}
