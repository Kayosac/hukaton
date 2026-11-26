using System.IO;
using System.Linq;
using System.Text;
using Hakaton.GodMountains;
using NUnit.Framework;
using UnityEngine;

namespace Hakaton.Game.Tests
{
    /// <summary>Balance stage 6: the god speaks of the shop's goal as it is — four hundred coins, not a thousand.</summary>
    public sealed class GodLineTests
    {
        const string Line = "Четыреста монет. Для тебя гора. Для меня пылинка на ногте.";

        static string Escaped(string text) => string.Concat(text.Select(c => c < 128 ? c.ToString() : "\\u" + ((int)c).ToString("X4")));

        [Test]
        public void TheGodSaysFourHundredInCodeAndInHisScene()
        {
            var host = new GameObject("God line test");
            try
            {
                var god = host.AddComponent<GodEncounter>();
                Assert.That(god.lines, Has.Member(Line));
                Assert.That(god.lines.Any(line => line.Contains("Тысяч") || line.Contains("тысяч")), Is.False);
            }
            finally { Object.DestroyImmediate(host); }
            // The scene keeps its own copy of the lines (YAML with \u escapes, wrapped).
            string scene = File.ReadAllText("Assets/GodMountains/Scenes/GodMountains.unity", Encoding.UTF8);
            string flat = System.Text.RegularExpressions.Regex.Replace(scene, @"\s*\n\s*", " ");
            Assert.That(flat, Does.Contain(Escaped("Четыреста")));
            Assert.That(flat, Does.Not.Contain(Escaped("Тысяча")));
            Assert.That(flat, Does.Not.Contain(Escaped("тысяч")));
        }
    }
}
