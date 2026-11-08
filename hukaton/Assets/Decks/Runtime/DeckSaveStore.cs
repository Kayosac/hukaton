using System;
using System.IO;
using UnityEngine;

namespace Hakaton.Decks.Unity
{
    /// <summary>Only workshop progression is persisted; the independent test outing is session-local.</summary>
    public static class DeckSaveStore
    {
        public static bool Load(string path,DeckWorkshop model,out string error)
        {
            error=null;
            try {
                if(!File.Exists(path))return false;
                if(model.TryRestore(JsonUtility.FromJson<WorkshopSave>(File.ReadAllText(path))))return true;
                error="Сохранение несовместимо. Открыта стартовая коллекция.";
            } catch(Exception e) when(e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
                error="Не удалось прочитать сохранение. Открыта стартовая коллекция.";
            }
            return false;
        }
        public static bool Save(string path,DeckWorkshop model,out string error)
        {
            error=null;
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temp=path+".tmp";
                File.WriteAllText(temp,JsonUtility.ToJson(model.Save(),true));
                if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);
                return true;
            } catch(Exception e) when(e is IOException || e is UnauthorizedAccessException) {
                error="Не удалось сохранить прогресс на диск.";return false;
            }
        }
    }
}
