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
    /// </summary>
    public sealed class CaveTutorial : MonoBehaviour, IThrowRules
    {
        [Header("Scene")]
        public ExpeditionWorld world;
        public PlayerControlGate control;
        public FirstPersonView view;
        public ScreenFade fade;
        public TutorialBubble bubble;
        [Tooltip("The plain goblin (no nappy, no dummy), hidden in the bush until the Strike.")]
        public CounterGoblin goblin;
        [Tooltip("The cave's geometry: shown only while the prologue runs.")]
        public GameObject geometry;
        public Transform start;

        [Header("Where each card is handed over")]
        public Transform rootsZone;
        public Transform waterZone, bushZone;
        [Min(.5f)] public float zoneRadius = 3.5f;

        [Header("Bush")]
        [Tooltip("The bush: any card landing on it shakes it; only Strike brings the goblin out.")]
        public Transform bush;
        [Tooltip("Where the goblin stops to talk, facing the player.")]
        public Transform goblinStop;
        [Min(.1f)] public float panicSeconds = 5, panicRadius = 1.7f, panicSpeed = 3.2f;
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
        public bool Active => Stage != CaveStage.Off && Stage != CaveStage.Done;
        /// <summary>The screen is dark and the player stands in the shop: the shop tutorial takes over.</summary>
        public event Action Finished;

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
            if (geometry) geometry.SetActive(true);
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
                case CaveStage.Water when Near(waterZone, player):
                    Grant(iceId);
                    wheelHintPending = true;
                    Stage = CaveStage.Bush;
                    break;
                case CaveStage.Bush when Near(bushZone, player) && !HasCard(strikeId):
                    Grant(strikeId);
                    break;
                case CaveStage.Talk:
                    var keyboard = Keyboard.current;
                    if (keyboard != null && keyboard.eKey.wasPressedThisFrame) continueRequested = true;
                    break;
            }
        }

        bool Near(Transform zone, Vector3 point)
        {
            if (!zone) return false;
            Vector3 offset = zone.position - point;
            offset.y = 0;
            return offset.magnitude <= zoneRadius;
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
            // He stops where the player can see him and starts talking.
            Vector3 stopFrom = body.position, stop = goblinStop ? goblinStop.position : body.position;
            time = 0;
            while (time < .3f)
            {
                time += Time.deltaTime;
                body.position = Vector3.Lerp(stopFrom, stop, time / .3f);
                yield return null;
            }
            yield return Talk();
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
            if (view) view.Guide(goblin.face.transform.position);
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
            goblin.Show(false);
            if (bush) bush.localPosition = bushRest;
            if (geometry) geometry.SetActive(false);
        }

        void OnDisable()
        {
            if (Active) { StopAllCoroutines(); Close(); Stage = CaveStage.Off; }
        }
    }
}
