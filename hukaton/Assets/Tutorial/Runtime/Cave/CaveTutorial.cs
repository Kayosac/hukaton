using System;
using System.Collections;
using System.Collections.Generic;
using Hakaton.Character;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Hakaton.Tutorial
{
    public enum CaveStage { Off, Instructions, Roots, Water, Bush, Ambush, Talk, Leaving, Done }

    /// <summary>
    /// The cave prologue (board, «Туториал · согласовано», 01–04, 09). The hero wakes in a linear cave with three
    /// instruction cards — throw, move, discard — used strictly in order (R only after two throws). Each obstacle
    /// hands over the card it needs: Fire at the roots, Ice at the flooded stretch, Strike at the bush. Spells come
    /// back after every throw. Only a Strike on the bush brings out the goblin: ~5 s of panic around the bush, then
    /// seven lines on E with the view held on him, and the screen goes dark into the shop.
    /// Nothing is saved in here: quitting the cave starts it again from the awakening.
    /// The place itself (<see cref="CaveLevel"/>) lives in its own scene with its own lighting: it is loaded next to the
    /// game scene when the prologue begins and unloaded on the way to the shop.
    /// </summary>
    public sealed class CaveTutorial : MonoBehaviour, IThrowRules
    {
        [Header("Level")]
        [Tooltip("The scene with the cave itself (CaveLevel inside), loaded next to this one for the prologue.")]
        public string caveScene = "TutorialCave";
        [Tooltip("What the goblin turns to face (the player's view).")]
        public Transform goblinViewer;

        [Header("Scene")]
        public ExpeditionWorld world;
        public PlayerControlGate control;
        public FirstPersonView view;
        public ScreenFade fade;
        public TutorialBubble bubble;

        [Header("Where each card is handed over")]
        [Min(.5f)] public float zoneRadius = 3.5f;
        [Tooltip("A card is handed over only within this height of its zone (the cave folds over itself).")]
        [Min(.5f)] public float zoneHeight = 2.5f;
        [Tooltip("What blocks the view to a zone: rock and props, not the player, cards or the water surface.")]
        public LayerMask sightBlockers = ~((1 << 2) | (1 << 4) | (1 << 8));

        [Header("Bush")]
        [Min(.1f)] public float panicSeconds = 5, panicRadius = 1.7f, panicSpeed = 3.2f;
        [Tooltip("He stops this far in front of the player, on the way to the bush, to talk.")]
        [Min(1)] public float talkDistance = 2.8f;
        [Tooltip("How far out of the bush's centre he stands to talk.")]
        [Min(0)] public float bushClearance = 1.5f;
        public AudioClip[] yelps;

        [Header("Cards")]
        public CardDefinition throwInstruction;
        public CardDefinition moveInstruction, discardInstruction;
        public string fireId = "fire", iceId = "ice", strikeId = "strike";
        [TextArea] public string wheelHint = "Колесо мыши — выбрать карту";

        [Header("Lines")]
        [TextArea] public string[] lines =
        {
            "Эй! Ты чего?! Тут вообще-то занято!",
            "Я тут работал. С закрытыми глазами.",
            "Погоди. Это у тебя карты?",
            "Карты есть… Лавка у меня есть…",
            "О! Будем партнёрами. Я уже согласен.",
            "Ты добываешь товар, я даю лавку. И ценные указания.",
            "Пойдём. Покажу, куда всё тащить."
        };
        [Tooltip("He stops to think before this line (he has just noticed the cards).")]
        public int thinkBefore = 3;
        [Tooltip("The bright idea: a jump and the pleased face.")]
        public int ideaLine = 4;
        [Min(0)] public float thinkSeconds = 1.6f;
        [Min(.05f)] public float darkSeconds = .7f;

        public CaveStage Stage { get; private set; } = CaveStage.Off;
        /// <summary>From the moment the cave's scene starts loading until the screen is dark on the way to the shop.</summary>
        public bool Active => loading || Stage != CaveStage.Off && Stage != CaveStage.Done;
        /// <summary>The loaded cave (null outside the prologue).</summary>
        public CaveLevel Level { get; private set; }
        /// <summary>The screen is dark and the player stands in the shop: the shop tutorial takes over.</summary>
        public event Action Finished;

        bool loading;
        Scene levelScene;
        AsyncOperation levelLoad;
        Transform start => Level ? Level.start : null;
        Transform rootsZone => Level ? Level.rootsZone : null;
        Transform waterZone => Level ? Level.waterZone : null;
        Transform bushZone => Level ? Level.bushZone : null;
        Transform bush => Level ? Level.bush : null;
        CounterGoblin goblin => Level ? Level.goblin : null;
        WorldObstacle rootsObstacle => Level ? Level.rootsObstacle : null;
        WorldObstacle waterObstacle => Level ? Level.waterObstacle : null;

        int instructionsThrown;
        bool continueRequested, wheelHintPending;
        float bushShake;
        Vector3 bushRest;
        readonly List<int> instructions = new List<int>();

        Expedition Trip => world.workshop.Model.Current;

        // ---------- start ----------

        public void Begin()
        {
            if (Active) return;
            StartCoroutine(LoadLevelAndBegin());
        }

        IEnumerator LoadLevelAndBegin()
        {
            loading = true;
            if (fade) fade.Set(1);
            levelLoad = SceneManager.LoadSceneAsync(caveScene, LoadSceneMode.Additive);
            if (levelLoad == null) { loading = false; Debug.LogError($"Cave prologue: scene '{caveScene}' is not in the build.", this); yield break; }
            yield return levelLoad;
            levelLoad = null;
            levelScene = SceneManager.GetSceneByName(caveScene);
            foreach (var root in levelScene.GetRootGameObjects())
                if (root.TryGetComponent<CaveLevel>(out var level)) { Level = level; break; }
            if (!Level) { loading = false; Debug.LogError($"Cave prologue: no CaveLevel at the root of '{caveScene}'.", this); yield break; }
            // the cave's own lighting (ambient, fog) comes with its scene
            SceneManager.SetActiveScene(levelScene);
            if (goblin) goblin.viewer = goblinViewer;
            world.prologueObstacles = new[] { rootsObstacle, waterObstacle };
            foreach (var obstacle in world.prologueObstacles) if (obstacle) obstacle.world = world;
            world.prologueShores = Level.shores;
            loading = false;
            StartInCave();
            if (fade) yield return fade.FadeTo(0, .5f);
        }

        void StartInCave()
        {
            goblin.Show(false);
            if (bush) bushRest = bush.localPosition;
            instructions.Clear();
            instructionsThrown = 0;
            wheelHintPending = false;
            string error = world.workshop.Model.DepartPrologue();
            if (error != null) { Debug.LogError("Cave prologue: " + error, this); return; }
            Stage = CaveStage.Instructions;
            world.thrower.rules = this;
            world.thrower.Thrown += OnThrown;
            world.thrower.SelectionChanged += OnSelectionChanged;
            world.Impacted += OnImpacted;
            world.BeginPrologue(start);
            foreach (var card in new[] { throwInstruction, moveInstruction, discardInstruction })
                instructions.Add(Trip.GrantToHand(card.ToSpec()).Id);
            world.thrower.SelectCopy(instructions[0]);
        }

        // ---------- rules for the hand ----------

        bool IsInstruction(CardCopy card) => instructions.Contains(card.Id);

        int CurrentInstruction
        {
            get
            {
                foreach (var card in Trip.Deck.Hand) if (IsInstruction(card)) return card.Id;
                return int.MinValue;
            }
        }

        public bool CanSelect(CardCopy card) => !IsInstruction(card) || card.Id == CurrentInstruction;

        // The discard drawing is used by R alone.
        public bool CanThrow(CardCopy card) => !IsInstruction(card) || card.Id != instructions[2];

        public bool CanDiscard() => Stage != CaveStage.Instructions || instructionsThrown >= 2;

        public void Discarded()
        {
            if (Stage == CaveStage.Instructions && instructionsThrown >= 2) Stage = CaveStage.Roots;
        }

        void OnThrown(CardCopy card)
        {
            if (IsInstruction(card)) instructionsThrown++;
        }

        void OnSelectionChanged()
        {
            // The player turned the wheel (or pressed a number) once: the hint has done its job.
            if (wheelHintPending && !granting) { wheelHintPending = false; world.Announce(string.Empty, 0); }
        }

        bool granting;

        void Grant(string id)
        {
            var definition = world.workshop.config.Find(id);
            if (!definition) { Debug.LogError($"Cave prologue: no card '{id}'.", this); return; }
            var copy = Trip.GrantToHand(definition.ToSpec());
            granting = true;
            world.thrower.SelectCopy(copy.Id);
            granting = false;
            world.Play(world.cues ? world.cues.returnAppears : null, null, .7f);
        }

        // ---------- the walk ----------

        void Update()
        {
            if (!Active) return;
            if (bush) Shake();
            if (wheelHintPending) world.Announce(wheelHint, .5f);
            Vector3 player = world.motor.transform.position;
            switch (Stage)
            {
                case CaveStage.Roots when Near(rootsZone, player):
                    Grant(fireId);
                    Stage = CaveStage.Water;
                    break;
                case CaveStage.Water when Passed(rootsObstacle) && Near(waterZone, player):
                    Grant(iceId);
                    wheelHintPending = true;
                    Stage = CaveStage.Bush;
                    break;
                case CaveStage.Bush when Passed(waterObstacle) && Near(bushZone, player) && !HasCard(strikeId):
                    Grant(strikeId);
                    break;
                case CaveStage.Talk:
                    var keyboard = Keyboard.current;
                    if (keyboard != null && keyboard.eKey.wasPressedThisFrame) continueRequested = true;
                    break;
            }
        }

        static bool Passed(WorldObstacle obstacle) => !obstacle || obstacle.Resolved;

        /// <summary>Within the zone's circle and height, and the zone is in plain view (not behind the next wall).</summary>
        bool Near(Transform zone, Vector3 point)
        {
            if (!zone) return false;
            Vector3 offset = zone.position - point;
            if (Mathf.Abs(offset.y) > zoneHeight) return false;
            offset.y = 0;
            if (offset.magnitude > zoneRadius) return false;
            Vector3 eye = world.viewCamera ? world.viewCamera.transform.position : point + Vector3.up * 1.6f;
            return !Physics.Linecast(eye, zone.position + Vector3.up * 1.2f, sightBlockers, QueryTriggerInteraction.Ignore);
        }

        bool HasCard(string id)
        {
            foreach (var card in Trip.Deck.Hand) if (card.Spec.Id == id) return true;
            foreach (var card in Trip.Deck.Held) if (card.Spec.Id == id) return true;
            return false;
        }

        void OnImpacted(CardCopy card, Collider target, Vector3 point)
        {
            if (!target) return;
            if (Stage == CaveStage.Ambush && goblin && target.transform.IsChildOf(goblin.transform)) { Yelp(); return; }
            if (!bush || !target.transform.IsChildOf(bush)) return;
            // Fire and Ice only rustle it; the Strike flushes out whoever sleeps inside.
            bushShake = 1;
            if (Stage == CaveStage.Bush && card.Spec.Id == strikeId) StartCoroutine(Ambush());
        }

        void Shake()
        {
            bushShake = Mathf.MoveTowards(bushShake, 0, Time.deltaTime * 2.2f);
            float wobble = Mathf.Sin(Time.time * 55) * .06f * bushShake;
            bush.localPosition = bushRest + new Vector3(wobble, Mathf.Abs(wobble) * .5f, -wobble * .6f);
        }

        // ---------- the goblin ----------

        IEnumerator Ambush()
        {
            Stage = CaveStage.Ambush;
            wheelHintPending = false;
            var body = goblin.transform;
            Vector3 centre = bush.position;
            Vector3 from = new Vector3(centre.x, body.position.y, centre.z);
            Vector3 toPlayer = world.motor.transform.position - centre;
            toPlayer.y = 0;
            float angle = Mathf.Atan2(toPlayer.z, toPlayer.x);
            goblin.Show(true);
            body.position = from;
            Yelp();
            // Out of the bush in one hop, then panicked laps around it.
            float time = 0;
            Vector3 lap = centre + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * panicRadius;
            lap.y = from.y;
            while (time < .35f)
            {
                time += Time.deltaTime;
                float k = time / .35f;
                body.position = Vector3.Lerp(from, lap, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * .7f;
                yield return null;
            }
            time = 0;
            while (time < panicSeconds)
            {
                time += Time.deltaTime;
                angle += panicSpeed / panicRadius * Time.deltaTime;
                goblin.face.Startle(.2f);
                body.position = new Vector3(centre.x + Mathf.Cos(angle) * panicRadius, from.y + Mathf.Abs(Mathf.Sin(time * 13)) * .16f,
                    centre.z + Mathf.Sin(angle) * panicRadius);
                yield return null;
            }
            // He stops in front of the player, between them and the bush, and starts talking.
            Vector3 player = world.motor.transform.position;
            Vector3 toBush = centre - player;
            toBush.y = 0;
            Vector3 along = toBush.sqrMagnitude > .25f ? toBush.normalized : Flat(world.viewCamera.transform.forward);
            // Out in front of the leaves, never on top of the player.
            float gap = Mathf.Clamp(toBush.magnitude - bushClearance, 1.3f, talkDistance);
            Vector3 stopFrom = body.position, stop = player + along * gap;
            stop.y = from.y;
            time = 0;
            while (time < .3f)
            {
                time += Time.deltaTime;
                body.position = Vector3.Lerp(stopFrom, stop, time / .3f);
                yield return null;
            }
            yield return Talk();
        }

        static Vector3 Flat(Vector3 direction)
        {
            direction.y = 0;
            return direction.sqrMagnitude > .0001f ? direction.normalized : Vector3.forward;
        }

        void Yelp()
        {
            goblin.face.Startle(.8f);
            goblin.Speak(true);
            if (goblin.audioSource && yelps != null && yelps.Length > 0)
                goblin.audioSource.PlayOneShot(yelps[UnityEngine.Random.Range(0, yelps.Length)], .8f);
        }

        IEnumerator Talk()
        {
            Stage = CaveStage.Talk;
            world.thrower.Cancel();
            control.SetBlocked(this, true);
            // A little above his face: he sits low in the frame, the bubble keeps the top.
            if (view) view.Guide(goblin.face.transform.position + Vector3.up * .35f);
            for (int i = 0; i < lines.Length; i++)
            {
                if (i == thinkBefore)
                {
                    // He has just noticed the cards: a pause to think it over.
                    bubble.Say("…", false);
                    yield return new WaitForSeconds(thinkSeconds);
                }
                bubble.Say(lines[i], true);
                goblin.Speak(i == 0 || i == ideaLine);
                if (i == ideaLine) goblin.face.Please();
                continueRequested = false;
                yield return null;
                while (!continueRequested) yield return null;
                continueRequested = false;
            }
            bubble.Hide();
            yield return Leave();
        }

        /// <summary>Straight out of the cave (cheats, tests): the same exit as after the last line, without the dark.</summary>
        public void Skip()
        {
            if (!Active) return;
            StopAllCoroutines();
            bubble.Hide();
            Close();
            Stage = CaveStage.Done;
            Finished?.Invoke();
        }

        /// <summary>Closes the line on screen, as E does.</summary>
        public void Continue()
        {
            if (Stage == CaveStage.Talk) continueRequested = true;
        }

        IEnumerator Leave()
        {
            Stage = CaveStage.Leaving;
            if (fade) yield return fade.FadeTo(1, darkSeconds);
            Close();
            Stage = CaveStage.Done;
            Finished?.Invoke();
        }

        void Close()
        {
            world.EndPrologue();
            world.thrower.rules = null;
            world.thrower.Thrown -= OnThrown;
            world.thrower.SelectionChanged -= OnSelectionChanged;
            world.Impacted -= OnImpacted;
            if (view) view.Guide(null);
            control.SetBlocked(this, false);
            if (goblin) goblin.Show(false);
            if (bush) bush.localPosition = bushRest;
            UnloadLevel();
        }

        void UnloadLevel()
        {
            loading = false;
            world.prologueObstacles = new WorldObstacle[0];
            world.prologueShores = new Transform[0];
            Level = null;
            if (SceneManager.GetActiveScene() != gameObject.scene) SceneManager.SetActiveScene(gameObject.scene);
            if (levelLoad != null)
            {
                // skipped while still loading: drop the scene as soon as it arrives
                string name = caveScene;
                levelLoad.completed += _ => SceneManager.UnloadSceneAsync(name);
                levelLoad = null;
            }
            else if (levelScene.IsValid() && levelScene.isLoaded) SceneManager.UnloadSceneAsync(levelScene);
            levelScene = default;
        }

        void OnDisable()
        {
            // Leaving Play or unloading the scene mid-cave: release the player, nothing else to keep.
            if (control) control.SetBlocked(this, false);
            if (view) view.Guide(null);
        }
    }
}
