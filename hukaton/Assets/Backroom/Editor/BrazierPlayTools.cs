using System.Linq;
using System.Reflection;
using Hakaton.Character;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Backroom.Editor
{
    /// <summary>Play-mode shortcuts for trying the brazier: stand in front of it and open it, throw a card in.</summary>
    public static class BrazierPlayTools
    {
        /// <summary>Trying the brazier must not cost the real save its cards: saving is switched off for this play.</summary>
        static void KeepSaveUntouched()
        {
            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                var field = behaviour.GetType().GetField("persistProgress");
                if (field != null && field.FieldType == typeof(bool)) field.SetValue(behaviour, false);
            }
        }

        [MenuItem("Tools/Hakaton/Brazier/Stand at the brazier and open it")]
        static void StandAndOpen()
        {
            KeepSaveUntouched();
            var brazier = Object.FindFirstObjectByType<Brazier>();
            var motor = Object.FindFirstObjectByType<FirstPersonMotor>();
            var view = Object.FindFirstObjectByType<FirstPersonView>();
            if (!brazier || !motor) { Debug.LogWarning("[Brazier] no brazier or player in play"); return; }
            var fire = brazier.firePoint.position;
            // The open side: the direction with the most room in front of the bowl and a floor under it.
            Vector3 stand = fire;
            float best = -1;
            for (int i = 0; i < 8; i++)
            {
                var dir = Quaternion.Euler(0, i * 45, 0) * Vector3.forward;
                float room = Physics.Raycast(fire + Vector3.up * .6f, dir, out var wall, 3) ? wall.distance : 3;
                var spot = fire + dir * 1.25f;
                if (!Physics.Raycast(spot + Vector3.up * 1.5f, Vector3.down, out var floor, 4)) continue;
                if (room > best) { best = room; stand = new Vector3(spot.x, floor.point.y + .05f, spot.z); }
            }
            motor.TeleportKeepingVelocity(stand);
            motor.SetVelocity(Vector3.zero);
            if (view) view.ResetPose(Quaternion.LookRotation(fire - (stand + Vector3.up * 1.5f)));
            Debug.Log($"[Brazier] open: {brazier.Open()} fire {fire} brazier fwd {brazier.transform.forward} stand {stand} cam {Camera.main?.transform.position}");
        }

        [MenuItem("Tools/Hakaton/Brazier/Throw the first burnable card")]
        static void ThrowFirst()
        {
            var brazier = Object.FindFirstObjectByType<Brazier>();
            if (!brazier || !brazier.IsOpen) { Debug.LogWarning("[Brazier] not open"); return; }
            KeepSaveUntouched();
            var tile = brazier.Tiles.FirstOrDefault(t => t.gameObject.activeSelf && t.group.blocksRaycasts);
            Debug.Log($"[Brazier] throwing {tile?.name} at {Time.frameCount}, owned {brazier.workshop.Model.Owned.Count}");
            if (!tile) { Debug.LogWarning("[Brazier] nothing to burn"); return; }
            typeof(Brazier).GetMethod("OnTileClicked", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(brazier, new object[] { tile });
        }

        [MenuItem("Tools/Hakaton/Brazier/Hover the first burnable card")]
        static void HoverFirst()
        {
            var brazier = Object.FindFirstObjectByType<Brazier>();
            var tile = brazier ? brazier.Tiles.FirstOrDefault(t => t.gameObject.activeSelf && t.group.blocksRaycasts) : null;
            if (!tile) return;
            tile.OnPointerEnter(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current));
        }

        [MenuItem("Tools/Hakaton/Brazier/Slow motion on-off")]
        static void Slow() => Time.timeScale = Time.timeScale < 1 ? 1 : .08f;

        [MenuItem("Tools/Hakaton/Brazier/Close")]
        static void Close() => Object.FindFirstObjectByType<Brazier>()?.Close();

        [MenuItem("Tools/Hakaton/Brazier/Hover the first burnable card", true)]
        [MenuItem("Tools/Hakaton/Brazier/Slow motion on-off", true)]
        [MenuItem("Tools/Hakaton/Brazier/Close", true)]
        [MenuItem("Tools/Hakaton/Brazier/Stand at the brazier and open it", true)]
        [MenuItem("Tools/Hakaton/Brazier/Throw the first burnable card", true)]
        static bool InPlay() => Application.isPlaying;
    }
}
