using System;
using System.Collections;
using Hakaton.Character;
using Hakaton.Decks;
using Hakaton.Expeditions;
using Hakaton.Feel;
using Hakaton.Tutorial;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hakaton.GodMountains
{
    public enum GodVisitStage { Off, Loading, Path, GoHome, Done }

    /// <summary>
    /// The one trip to the god (docs/level-design/god-mountains, chapter 2), started from the «?» on the outing map.
    /// Runs like the cave prologue: the level's scene is loaded next to the game scene in the dark and made active (its
    /// own fog, sky and sun), the outing goes without the deck, hearts hidden and no death. When the god has said his
    /// lines the hand holds only «Пора домой»; it is the only thing the player can do. Its fling and dark end the visit:
    /// the player stands in the shop, the scene is unloaded and <see cref="Finished"/> says whether he was met.
    /// Nothing is saved in here: quitting midway leaves the god unmet and the «?» on the map.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GodMountainsVisit : MonoBehaviour, IThrowRules
    {
        [Header("Level")]
        [Tooltip("The scene with GodMountainsLevel at its root, loaded next to this one for the visit.")]
        public string levelScene = "GodMountains";

        [Header("Scene")]
        public ExpeditionWorld world;
        public PlayerControlGate control;
        public FirstPersonView view;
        public ScreenFade fade;
        [Tooltip("The linen plate with typed lines (the cave goblin's): the god speaks on it.")]
        public TutorialBubble bubble;
        [Tooltip("The game's own sun: off while here, the level has its sun facing the god.")]
        public Light gameSun;
        [Tooltip("Game scene objects the far camera could see from the mountains (the authored valley): off while here.")]
        public GameObject[] hideWhileHere = new GameObject[0];

        [Header("Feel")]
        [Tooltip("The name on the map's card: the player must not learn where he is going.")]
        public string mapName = "???";
        [Tooltip("How long the screen stays dark in the pocket before it opens (the first breath is heard).")]
        [Min(0)] public float darkSeconds = 1.5f;
        [Min(.05f)] public float openSeconds = .8f;
        [TextArea] public string returnHint = "ЛКМ — вернуться в лавку";

        public GodVisitStage Stage { get; private set; } = GodVisitStage.Off;
        /// <summary>From the moment the level starts loading until the player stands in the shop again.</summary>
        public bool Active => Stage != GodVisitStage.Off && Stage != GodVisitStage.Done;
        /// <summary>The screen is dark and the player stands in the shop. True: the god was met (he gave «Пора домой» and it was used).</summary>
        public event Action<bool> Finished;
        /// <summary>The loaded level (null outside the visit).</summary>
        public GodMountainsLevel Level { get; private set; }

        Scene levelSceneLoaded;
        AsyncOperation levelLoad;
        Camera viewCamera;
        float savedFar;
        CameraClearFlags savedClear;
        Color savedBackground;
        bool sunWasOn, heartsWereOn, cameraSaved;
        bool[] hiddenWereOn = new bool[0];

        Expedition Trip => world.workshop.Model.Current;

        public void Begin()
        {
            if (Active) return;
            StartCoroutine(LoadAndBegin());
        }

        IEnumerator LoadAndBegin()
        {
            Stage = GodVisitStage.Loading;
            if (fade) fade.Set(1);
            GodMountainsLevel.Hosted = true;
            levelLoad = SceneManager.LoadSceneAsync(levelScene, LoadSceneMode.Additive);
            if (levelLoad == null)
            {
                Debug.LogError($"God mountains: scene '{levelScene}' is not in the build.", this);
                Abort();
                yield break;
            }
            yield return levelLoad;
            levelLoad = null;
            levelSceneLoaded = SceneManager.GetSceneByName(levelScene);
            foreach (var root in levelSceneLoaded.GetRootGameObjects())
                if (root.TryGetComponent<GodMountainsLevel>(out var level)) { Level = level; break; }
            if (!Level || !Level.encounter || !Level.spawn)
            {
                Debug.LogError($"God mountains: no GodMountainsLevel (with its encounter and spawn) at the root of '{levelScene}'.", this);
                Abort();
                yield break;
            }
            string error = world.workshop.Model.DepartPrologue();
            if (error != null)
            {
                Debug.LogError("God mountains: " + error, this);
                Abort();
                yield break;
            }
            // the level's own fog, sky and ambient come with its scene; the far ranges and the god need a far camera
            SceneManager.SetActiveScene(levelSceneLoaded);
            viewCamera = world.viewCamera;
            if (viewCamera)
            {
                savedFar = viewCamera.farClipPlane;
                savedClear = viewCamera.clearFlags;
                savedBackground = viewCamera.backgroundColor;
                cameraSaved = true;
                viewCamera.farClipPlane = Mathf.Max(savedFar, Level.farClip);
            }
            if (gameSun) { sunWasOn = gameSun.enabled; gameSun.enabled = false; }
            hiddenWereOn = new bool[hideWhileHere.Length];
            for (int i = 0; i < hideWhileHere.Length; i++)
                if (hideWhileHere[i]) { hiddenWereOn[i] = hideWhileHere[i].activeSelf; hideWhileHere[i].SetActive(false); }
            if (Level.atmosphere)
            {
                Level.atmosphere.viewer = viewCamera ? viewCamera.transform : world.motor.transform;
                Level.atmosphere.viewCamera = viewCamera;
            }
            world.thrower.rules = this;
            world.IgnoreKillHeight = true;
            world.BeginPrologue(Level.spawn);
            // no hearts here: nothing can hurt the player
            if (world.hud && world.hud.hearts)
            {
                heartsWereOn = world.hud.hearts.gameObject.activeSelf;
                world.hud.hearts.gameObject.SetActive(false);
            }
            Level.encounter.finished.AddListener(OnTalkOver);
            Level.encounter.Bind(world.motor, view, control, bubble);
            Stage = GodVisitStage.Path;
            if (darkSeconds > 0) yield return new WaitForSecondsRealtime(darkSeconds);
            if (fade) yield return fade.FadeTo(0, openSeconds);
        }

        // ---------- the hand ----------

        // Only «Пора домой» exists here, and it can only be thrown.
        public bool CanSelect(CardCopy card) => card != null && card.IsReturn;
        public bool CanThrow(CardCopy card) => card != null && card.IsReturn;
        public bool CanDiscard() => false;
        public void Discarded() { }

        /// <summary>He has said everything: the hand holds «Пора домой» and nothing else can be done.</summary>
        void OnTalkOver()
        {
            if (Stage != GodVisitStage.Path) return;
            Stage = GodVisitStage.GoHome;
            var card = Trip?.GrantReturn();
            if (card == null)
            {
                Debug.LogError("God mountains: the Return card could not be handed over; going home at once.", this);
                StartCoroutine(LeaveInTheDark());
                return;
            }
            world.thrower.SelectCopy(card.Id);
            world.PrologueReturned += OnReturnUsed;
        }

        void Update()
        {
            if (Stage == GodVisitStage.GoHome && !world.Returning && Time.time >= world.NoticeUntil - .05f) world.Announce(returnHint, .5f);
        }

        /// <summary>«Пора домой» was used and the screen is dark: out of the mountains into the shop.</summary>
        void OnReturnUsed()
        {
            world.PrologueReturned -= OnReturnUsed;
            if (Stage != GodVisitStage.GoHome) return;
            Close();
            Stage = GodVisitStage.Done;
            Finished?.Invoke(true);
        }

        IEnumerator LeaveInTheDark()
        {
            if (fade) yield return fade.FadeTo(1, .6f);
            Close();
            Stage = GodVisitStage.Done;
            Finished?.Invoke(true);
        }

        /// <summary>Something is missing: back to the shop in the dark, the god stays unmet.</summary>
        void Abort()
        {
            Close();
            Stage = GodVisitStage.Done;
            Finished?.Invoke(false);
        }

        void Close()
        {
            world.PrologueReturned -= OnReturnUsed;
            if (Level && Level.encounter)
            {
                Level.encounter.finished.RemoveListener(OnTalkOver);
                Level.encounter.Release();
            }
            world.EndPrologue();
            world.IgnoreKillHeight = false;
            if (world.thrower.rules == (IThrowRules)this) world.thrower.rules = null;
            if (world.hud && world.hud.hearts && heartsWereOn) world.hud.hearts.gameObject.SetActive(true);
            heartsWereOn = false;
            if (view) view.Guide(null);
            if (control) control.SetBlocked(this, false);
            RestoreCamera();
            if (gameSun) gameSun.enabled = sunWasOn || gameSun.enabled;
            sunWasOn = false;
            for (int i = 0; i < hiddenWereOn.Length && i < hideWhileHere.Length; i++)
                if (hideWhileHere[i] && hiddenWereOn[i]) hideWhileHere[i].SetActive(true);
            hiddenWereOn = new bool[0];
            UnloadLevel();
        }

        void RestoreCamera()
        {
            if (!cameraSaved || !viewCamera) return;
            viewCamera.farClipPlane = savedFar;
            viewCamera.clearFlags = savedClear;
            viewCamera.backgroundColor = savedBackground;
            cameraSaved = false;
        }

        void UnloadLevel()
        {
            // Off first: its air must not write into the game scene's lighting once that is active again.
            if (Level) Level.gameObject.SetActive(false);
            if (Level && Level.atmosphere) Level.atmosphere.enabled = false;
            Level = null;
            if (SceneManager.GetActiveScene() != gameObject.scene) SceneManager.SetActiveScene(gameObject.scene);
            if (levelLoad != null)
            {
                string name = levelScene;
                levelLoad.completed += _ => SceneManager.UnloadSceneAsync(name);
                levelLoad = null;
            }
            else if (levelSceneLoaded.IsValid() && levelSceneLoaded.isLoaded) SceneManager.UnloadSceneAsync(levelSceneLoaded);
            levelSceneLoaded = default;
            GodMountainsLevel.Hosted = false;
        }

        void OnDisable()
        {
            // Leaving Play or unloading the scene midway: release the player, nothing else to keep.
            if (control) control.SetBlocked(this, false);
            if (view) view.Guide(null);
            GodMountainsLevel.Hosted = false;
        }
    }
}
