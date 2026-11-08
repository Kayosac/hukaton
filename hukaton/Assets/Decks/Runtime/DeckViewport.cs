using UnityEngine;
namespace Hakaton.Decks.Unity
{
    /// <summary>Integer pixels only. The authored layout expands; fonts are never scaled.</summary>
    [ExecuteAlways]
    public sealed class DeckViewport : MonoBehaviour
    {
        public RectTransform canvasRect, content;
        public GameObject smallWindowNotice;
        public Vector2 maximumSize=new Vector2(1440,920);
        void LateUpdate()
        {
            if(!canvasRect || !content)return;
            var size=canvasRect.rect.size;
            content.sizeDelta=new Vector2(Mathf.Round(Mathf.Clamp(size.x-48,1232,maximumSize.x)),Mathf.Round(Mathf.Clamp(size.y-32,768,maximumSize.y)));
            if(smallWindowNotice)smallWindowNotice.SetActive(size.x<1280 || size.y<800);
        }
    }
}
