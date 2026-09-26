using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace pi.vrcalc
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ScrollRectHandler : UdonSharpBehaviour
    {
        [SerializeField] private ScrollRect _scrollRect;
        [SerializeField] private RectTransform _rowLabels;
        [SerializeField] private RectTransform _columnLabels;

        public void OnScrollBarPointerEnter()
        {
            _scrollRect.enabled = true;
        }

        public void OnScrollBarPointerExit()
        {
            _scrollRect.enabled = false;
        }

        public void OnScrollRectVectorChange()
        {
            _rowLabels.anchoredPosition = new Vector2(_rowLabels.anchoredPosition.x, _scrollRect.content.anchoredPosition.y);
            _columnLabels.anchoredPosition = new Vector2(_scrollRect.content.anchoredPosition.x, _columnLabels.anchoredPosition.y);
        }
    }
}