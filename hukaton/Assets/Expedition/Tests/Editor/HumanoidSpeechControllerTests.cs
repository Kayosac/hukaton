using System;
using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hakaton.Expeditions.Tests
{
    public sealed class HumanoidSpeechControllerTests
    {
        CreatureSpeechProfile profile;
        CreatureSpeechController controller;

        [SetUp]
        public void SetUp()
        {
            profile = ScriptableObject.CreateInstance<CreatureSpeechProfile>();
            profile.greetings = new[] { "Привет." };
            profile.neutral = new[] { "Хороший денёк." };
            profile.fear = new[] { "Не надо!" };
            profile.aggression = new[] { "Уходи!" };
            profile.attack = new[] { "Лови!" };
            profile.initialDelay = Vector2.zero;
            profile.displaySeconds = 1;
            profile.encounterFollowupDelay = .5f;
            profile.minimumReadSeconds = .2f;
            profile.reactionPause = Vector2.one * .5f;
            profile.reactionMemorySeconds = 2;
            profile.reencounterDelay = 2;
            controller = new CreatureSpeechController(new System.Random(42));
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(profile);

        bool Step(float at, CreatureSpeechMood? mood = CreatureSpeechMood.Neutral, bool available = true, bool inRange = true)
            => controller.Step(at, profile, mood, available, inRange);

        [Test]
        public void QuietEncounterSaysGreetingAndOneRemarkThenStaysSilent()
        {
            Assert.That(Step(0), Is.True);
            Assert.That(controller.CurrentLine, Is.EqualTo("Привет."));
            Assert.That(Step(.4f), Is.False);
            Assert.That(Step(1.01f), Is.True);
            Assert.That(controller.CurrentLine, Is.Null);
            Step(1.49f);
            Assert.That(controller.CurrentLine, Is.Null, "Leave a readable gap between the two lines.");
            Step(1.51f);
            Assert.That(controller.CurrentLine, Is.EqualTo("Хороший денёк."));
            Step(2.52f);
            Assert.That(controller.CurrentLine, Is.Null);
            Step(20);
            Assert.That(controller.CurrentLine, Is.Null, "Standing nearby must not restart idle chatter.");
        }

        [Test]
        public void InitialDelayAlsoAppliesToCombatAndUnavailableCreaturesRemainSilent()
        {
            profile.initialDelay = Vector2.one * .5f;
            Step(0, CreatureSpeechMood.Attack, false, false);
            Assert.That(controller.CurrentLine, Is.Null);
            Step(1, CreatureSpeechMood.Attack);
            Assert.That(controller.CurrentLine, Is.Null);
            Step(1.49f, CreatureSpeechMood.Attack);
            Assert.That(controller.CurrentLine, Is.Null);
            Step(1.51f, CreatureSpeechMood.Attack);
            Assert.That(controller.CurrentLine, Is.EqualTo("Лови!"));
        }

        [Test]
        public void OnlyASustainedDepartureAllowsAnotherGreeting()
        {
            FinishQuietEncounter();
            Step(3, available: false, inRange: false);
            Step(3.5f);
            Assert.That(controller.CurrentLine, Is.Null, "Crossing the radius briefly must not spam greetings.");
            Step(4, available: false, inRange: false);
            Step(6.01f);
            Assert.That(controller.CurrentLine, Is.EqualTo("Привет."));
        }

        [Test]
        public void LongOcclusionDoesNotCountAsAnotherMeeting()
        {
            FinishQuietEncounter();
            Step(3, available: false);
            Step(30, available: false);
            Step(31);
            Assert.That(controller.CurrentLine, Is.Null, "Walking behind a tree must not restart the conversation.");
        }

        [Test]
        public void LeavingRangeHidesImmediatelyWithoutReplayingTheCurrentLine()
        {
            Step(0);
            Assert.That(Step(.1f, available: false, inRange: false), Is.True);
            Assert.That(controller.CurrentLine, Is.Null);
            Step(.2f);
            Assert.That(controller.CurrentLine, Is.Null);
            Step(1.51f);
            Assert.That(controller.CurrentLine, Is.EqualTo("Хороший денёк."), "The encounter continues with its remaining remark.");
        }

        [Test]
        public void ReactionWaitsForMinimumReadingTimeAndCombatLinesAreNotInterrupted()
        {
            Step(0);
            Step(.1f, CreatureSpeechMood.Attack);
            Assert.That(controller.CurrentLine, Is.EqualTo("Привет."));
            Step(.21f, CreatureSpeechMood.Attack);
            Assert.That(controller.CurrentLine, Is.EqualTo("Лови!"));
            Step(.3f, CreatureSpeechMood.Aggression);
            Assert.That(controller.CurrentLine, Is.EqualTo("Лови!"));
            Step(1.22f, CreatureSpeechMood.Aggression);
            Assert.That(controller.CurrentLine, Is.Null, "A pause follows the combat line.");
            Step(1.72f, CreatureSpeechMood.Aggression);
            Assert.That(controller.CurrentLine, Is.EqualTo("Уходи!"));
        }

        [Test]
        public void CombatCooldownSurvivesLostVisibility()
        {
            Step(0, CreatureSpeechMood.Attack);
            Step(.1f, CreatureSpeechMood.Attack, false);
            Assert.That(controller.CurrentLine, Is.Null);
            Step(.2f, CreatureSpeechMood.Attack);
            Assert.That(controller.CurrentLine, Is.Null);
            Step(1.49f, CreatureSpeechMood.Attack);
            Assert.That(controller.CurrentLine, Is.Null);
            Step(1.51f, CreatureSpeechMood.Attack);
            Assert.That(controller.CurrentLine, Is.EqualTo("Лови!"));
        }

        [Test]
        public void ReturningToNeutralCancelsAnUnspokenAttackAndItsIdleFollowup()
        {
            Step(0);
            Step(.1f, CreatureSpeechMood.Attack);
            Step(.15f, CreatureSpeechMood.Neutral);
            Step(.5f);
            Assert.That(controller.CurrentLine, Is.EqualTo("Привет."));
            Step(1.01f);
            Step(10);
            Assert.That(controller.CurrentLine, Is.Null, "No stale attack or abandoned encounter remark after combat ends.");
        }

        [Test]
        public void ABriefAttackIsRememberedThroughTheFollowingChase()
        {
            Step(0);
            Step(.05f, CreatureSpeechMood.Attack);
            Step(.1f, CreatureSpeechMood.Aggression);
            Step(.21f, CreatureSpeechMood.Aggression);
            Assert.That(controller.CurrentLine, Is.EqualTo("Лови!"), "Do not lose a short attack state before the bubble can speak.");
        }

        [Test]
        public void EmptyGreetingSkipsToOneRemarkAndEmptyPoolsStaySilent()
        {
            profile.greetings = new[] { null, " ", "" };
            Step(0);
            Assert.That(controller.CurrentLine, Is.EqualTo("Хороший денёк."));
            Step(2);
            Assert.That(controller.CurrentLine, Is.Null);
            profile.attack = Array.Empty<string>();
            Step(3, CreatureSpeechMood.Attack);
            Step(30, CreatureSpeechMood.Attack);
            Assert.That(controller.CurrentLine, Is.Null);
        }

        [Test]
        public void SelectionIgnoresWhitespaceAndAvoidsConsecutiveDuplicateText()
        {
            profile.attack = new[] { null, " ", " Первый! ", "Первый!", "Второй!" };
            string previous = null;
            for (int i = 0; i < 20; i++)
            {
                Step(i * 2, CreatureSpeechMood.Attack);
                Assert.That(controller.CurrentLine, Is.EqualTo("Первый!").Or.EqualTo("Второй!"));
                if (previous != null) Assert.That(controller.CurrentLine, Is.Not.EqualTo(previous));
                previous = controller.CurrentLine;
            }
        }

        [Test]
        public void OutingResetStartsANewEncounterAndMissingProfileIsSafe()
        {
            FinishQuietEncounter();
            controller.Reset();
            Assert.That(controller.CurrentLine, Is.Null);
            Assert.That(controller.CurrentMood, Is.Null);
            Step(3);
            Assert.That(controller.CurrentLine, Is.EqualTo("Привет."));
            Assert.That(controller.Step(4, null, CreatureSpeechMood.Neutral, true), Is.True);
            Assert.That(controller.CurrentLine, Is.Null);
        }

        [Test]
        public void StateMappingIncludesRangedAttacksAndKeepsRecoveryInItsCombatContext()
        {
            Assert.That(CreatureSpeechController.MoodFor(CreatureState.Idle), Is.EqualTo(CreatureSpeechMood.Neutral));
            Assert.That(CreatureSpeechController.MoodFor(CreatureState.Wander), Is.EqualTo(CreatureSpeechMood.Neutral));
            Assert.That(CreatureSpeechController.MoodFor(CreatureState.Flee), Is.EqualTo(CreatureSpeechMood.Fear));
            Assert.That(CreatureSpeechController.MoodFor(CreatureState.Chase), Is.EqualTo(CreatureSpeechMood.Aggression));
            Assert.That(CreatureSpeechController.MoodFor(CreatureState.Windup), Is.EqualTo(CreatureSpeechMood.Attack));
            Assert.That(CreatureSpeechController.MoodFor(CreatureState.Strike), Is.EqualTo(CreatureSpeechMood.Attack));
            Assert.That(CreatureSpeechController.MoodFor(CreatureState.Aim), Is.EqualTo(CreatureSpeechMood.Attack));
            Assert.That(CreatureSpeechController.MoodFor(CreatureState.Shoot), Is.EqualTo(CreatureSpeechMood.Attack));
            Assert.That(CreatureSpeechController.MoodFor(CreatureState.Recover, CreatureSpeechMood.Attack), Is.EqualTo(CreatureSpeechMood.Attack));
            foreach (var state in new[] { CreatureState.Dying, CreatureState.Frozen, CreatureState.Stunned })
                Assert.That(CreatureSpeechController.MoodFor(state), Is.Null, state.ToString());
        }

        void FinishQuietEncounter()
        {
            Step(0);
            Step(1.51f);
            Step(2.52f);
            Assert.That(controller.CurrentLine, Is.Null);
        }
    }
}
