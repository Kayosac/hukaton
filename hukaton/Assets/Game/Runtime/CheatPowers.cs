using Hakaton.Character;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Hakaton.Game
{
    /// <summary>
    /// Two more cheats beside the F1 menu, in a panel of their own to its right: invulnerability (no damage, no
    /// drowning, no fall into the void) and free flight (fly through everything: WASD along the view, Space up,
    /// Ctrl or C down, Shift faster). Both stay on until switched off, across outings. Created next to the
    /// <see cref="GameLoop"/> when the scene has none.
    /// </summary>
    public sealed class CheatPowers : MonoBehaviour, IExternalMotorControl
    {
        public GameLoop game;
        [Min(1)] public float flySpeed = 14;
        [Min(1)] public float fastFlySpeed = 40;

        public bool Invulnerable { get; private set; }
        public bool Flying { get; private set; }

        CheatMenu menu;
        bool flightHeld;
        GUIStyle title, body, button;

        static bool hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (!hooked) { hooked = true; SceneManager.sceneLoaded += (_, __) => EnsureExists(); }
            EnsureExists();
        }

        static void EnsureExists()
        {
            if (FindFirstObjectByType<CheatPowers>(FindObjectsInactive.Include)) return;
            var loop = FindFirstObjectByType<GameLoop>();
            if (!loop) return;
            var go = new GameObject("Cheat powers (auto)");
            go.SetActive(false);
            go.AddComponent<CheatPowers>().game = loop;
            go.SetActive(true);
        }

        // ---------- the cheats (public so tests and tools can switch them) ----------

        public void SetInvulnerable(bool on)
        {
            Invulnerable = on;
            if (game && game.expedition && game.expedition.vitals) game.expedition.vitals.Immortal = on;
        }

        public void SetFlying(bool on)
        {
            Flying = on;
            if (!on) Land();
        }

        void Update()
        {
            if (!game) return;
            // Keep the flag on the vitals even if they were rebuilt or restored.
            var vitals = game.expedition ? game.expedition.vitals : null;
            if (vitals && vitals.Immortal != Invulnerable) vitals.Immortal = Invulnerable;
            if (Flying) Fly();
        }

        void Fly()
        {
            var motor = game.player;
            if (!motor) return;
            if (!flightHeld)
            {
                // Someone else (a rope) has the body: wait for it to let go.
                if (!motor.TryClaimExternalControl(this)) return;
                flightHeld = true;
            }
            if (motor.characterController.enabled) motor.characterController.enabled = false;
            var keys = Keyboard.current;
            var view = motor.view && motor.view.cameraTransform ? motor.view.cameraTransform : motor.transform;
            Vector2 move = motor.input ? motor.input.Move : Vector2.zero;
            float up = 0;
            bool blocked = menu && menu.IsOpen;
            if (keys != null && !blocked)
            {
                if (keys.spaceKey.isPressed) up += 1;
                if (keys.leftCtrlKey.isPressed || keys.cKey.isPressed) up -= 1;
            }
            var direction = view.forward * move.y + view.right * move.x + Vector3.up * up;
            if (direction.sqrMagnitude > 1) direction.Normalize();
            float speed = keys != null && keys.leftShiftKey.isPressed ? fastFlySpeed : flySpeed;
            motor.transform.position += direction * speed * Time.unscaledDeltaTime;
        }

        void Land()
        {
            var motor = game ? game.player : null;
            if (!motor || !flightHeld) return;
            flightHeld = false;
            motor.characterController.enabled = true;
            motor.ReleaseExternalControl(this, Vector3.zero);
        }

        /// <summary>A teleport or the end of an outing took the body: give it back; flight takes it again next frame.</summary>
        public Vector3 OnControlRevoked()
        {
            flightHeld = false;
            if (game && game.player) game.player.characterController.enabled = true;
            return Vector3.zero;
        }

        void OnDisable() => Land();

        // ---------- panel beside the F1 menu ----------

        void OnGUI()
        {
            if (!menu) menu = FindFirstObjectByType<CheatMenu>();
            if (!menu || !menu.IsOpen) return;
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { font = menu.titleFont, fontSize = 32, wordWrap = false };
                body = new GUIStyle(GUI.skin.label) { font = menu.bodyFont, fontSize = 18, wordWrap = true };
                button = new GUIStyle(GUI.skin.button) { font = menu.bodyFont, fontSize = 18, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 10, 6, 6) };
                title.normal.textColor = new Color(.95f, .86f, .55f);
                body.normal.textColor = new Color(.92f, .9f, .84f);
            }
            float scale = menu.pixelScale * Hakaton.Feel.UiReference.ScreenScale; // laid out for Full HD
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            var rect = new Rect(menu.panelPosition.x + menu.panelSize.x + 8, menu.panelPosition.y, 340, 250);
            GUI.Box(rect, GUIContent.none);
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 12, rect.y + 8, rect.width - 24, rect.height - 16));
            GUILayout.Label("Игрок", title);
            if (GUILayout.Button("Неуязвимость: " + (Invulnerable ? "ВКЛ" : "выкл"), button)) SetInvulnerable(!Invulnerable);
            if (GUILayout.Button("Свободный полёт: " + (Flying ? "ВКЛ" : "выкл"), button)) SetFlying(!Flying);
            GUILayout.Label("Полёт: WASD по взгляду, Пробел вверх, Ctrl или C вниз, Shift быстрее. Сквозь всё.", body);
            GUILayout.EndArea();
        }
    }
}
