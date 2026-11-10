using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace Hakaton.Decks.Unity
{
    public sealed class DeckRowView : MonoBehaviour
    {
        public Button button;
        public TMP_Text title, level;
        public Image marker;
        public int CopyId { get; private set; }
        public void Bind(CardCopy copy, Color color, bool selected)
        {
            CopyId = copy?.Id ?? 0;
            title.text = copy == null ? "—" : $"{copy.Id:00}  {copy.Spec.Name}";
            level.text = copy == null ? "" : CardCopy.UpgradeMark(copy.Level);
            marker.color = copy == null ? new Color(.17f, .20f, .18f) : color;
            button.interactable = copy != null;
            title.color = selected ? new Color(.95f, .80f, .49f)
                : copy == null     ? new Color(.34f, .39f, .35f)
                                   : new Color(.80f, .82f, .74f);
        }
    }
}
