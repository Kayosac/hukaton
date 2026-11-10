using System;
using System.IO;
using Hakaton.Decks;
using Hakaton.Trading;
using UnityEngine;

namespace Hakaton.Game
{
    [Serializable]
    public sealed class GameSave
    {
        public int version = 1;
        public int coins;
        public bool goalCelebrated;
        public WorkshopSave cards;
        public TradingSave shop;
        /// <summary>The goblin card dealer: his current ten, its reveal, purchases and when he may come again. Null in old saves.</summary>
        public GoblinSave goblin;
        /// <summary>The shop tutorial's progress. Null in saves made before it: the tutorial counts as done.</summary>
        public Hakaton.Tutorial.TutorialSave tutorial;
    }

    /// <summary>One safe-shop snapshot for the entire economy; physical outings are not resumed halfway.</summary>
    public static class GameSaveStore
    {
        public static bool Save(string path, GameSave save, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(save, true));
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            { error = "Не удалось сохранить игру: " + e.Message; return false; }
        }

        public static bool Load(string path, Func<GameSave, bool> validate, out GameSave save, out string warning)
        {
            save = null; warning = null;
            if (!File.Exists(path) && !File.Exists(path + ".bak")) return false;
            if (TryRead(path, validate, out save)) return true;
            if (TryRead(path + ".bak", validate, out save))
            { warning = "Загружена резервная копия прогресса."; return true; }
            warning = "Сохранение повреждено. Начат новый сеанс; исходные файлы сохранены.";
            return false;
        }

        static bool TryRead(string path, Func<GameSave, bool> validate, out GameSave save)
        {
            save = null;
            try
            {
                if (!File.Exists(path)) return false;
                var candidate = JsonUtility.FromJson<GameSave>(File.ReadAllText(path));
                if (!validate(candidate)) return false;
                save = candidate; return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            { return false; }
        }

        public static bool PreserveInvalidFiles(string path, out string error)
        {
            error = null;
            try
            {
                string suffix = ".invalid-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                foreach (string source in new[] { path, path + ".bak" })
                    if (File.Exists(source)) File.Move(source, source + suffix);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            { error = "Не удалось сохранить копию повреждённого файла. Автосохранение этого сеанса отключено: " + e.Message; return false; }
        }
    }
}
