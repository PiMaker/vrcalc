using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace pi.vrcalc
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TabBarHandler : UdonSharpBehaviour
    {
        [SerializeField] private GameObject[] _tabs;
        [SerializeField] private Image[] _buttons;

        [Space]
        [SerializeField] private Color _activeColor = Color.white;
        [SerializeField] private Color _inactiveColor = Color.gray;

        private void Start()
        {
            if (_tabs.Length != _buttons.Length)
            {
                Debug.LogError("TabBarHandler: _tabs and _buttons arrays must have the same length.");
                this.enabled = false;
                return;
            }

            HandleTabButton(0);
        }

        private void HandleTabButton(int tabIndex)
        {
            for (int i = 0; i < _tabs.Length; i++)
            {
                _tabs[i].SetActive(i == tabIndex);
                _buttons[i].color = i == tabIndex ? _activeColor : _inactiveColor;
            }
        }

        public void HandleTabButton0() => HandleTabButton(0);
        public void HandleTabButton1() => HandleTabButton(1);
        public void HandleTabButton2() => HandleTabButton(2);
        public void HandleTabButton3() => HandleTabButton(3);
        public void HandleTabButton4() => HandleTabButton(4);
        public void HandleTabButton5() => HandleTabButton(5);
        public void HandleTabButton6() => HandleTabButton(6);
    }
}