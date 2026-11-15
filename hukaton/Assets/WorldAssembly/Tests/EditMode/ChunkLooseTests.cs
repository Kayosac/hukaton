using Hakaton.WorldAssembly.EditorTools;
using NUnit.Framework;

namespace Hakaton.WorldAssembly.Tests
{
    public sealed class ChunkLooseTests
    {
        [Test]
        public void NothingFixedDependsOnALooseThing()
        {
            // A flame on a campfire's sticks or a hatchet on a log would hang in the air once the stick or log is thrown off.
            var problems = ChunkLoose.Problems(out int props);
            UnityEngine.Debug.Log($"[World] {props} loose things checked, {problems.Count} problems\n" + string.Join("\n", problems));
            Assert.That(props, Is.GreaterThan(100));
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [TestCase("Assets/OldApiary/Prefabs/Old Apiary.prefab")]
        [TestCase("Assets/TutorialCave/Prefabs/TutorialCave_Set.prefab")]
        public void NothingFixedDependsOnALooseThingInTheHandMadePlaces(string place)
        {
            var problems = ChunkLoose.PlaceProblems(place, out int props);
            UnityEngine.Debug.Log($"[World] {place}: {props} loose things checked, {problems.Count} problems\n" + string.Join("\n", problems));
            Assert.That(props, Is.GreaterThan(30));
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }
    }
}
