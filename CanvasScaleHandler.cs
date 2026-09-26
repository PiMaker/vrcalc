using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace pi.vrcalc
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class CanvasScaleHandler : UdonSharpBehaviour
    {
        [SerializeField] private Transform _canvas;
        [SerializeField] private float _scaleFactor = 0.1f;
        [SerializeField] private float _initScale = 0.5f;
        [SerializeField] private Slider _scaleSlider;

        private float _startScale;

        private void Start()
        {
            _startScale = _canvas.localScale.x;
            _scaleSlider.value = _initScale;
        }

        public void SliderValueChanged()
        {
            var newScale = Mathf.Lerp(_startScale * _scaleFactor, _startScale, _scaleSlider.value);
            _canvas.localScale = new Vector3(newScale, newScale, newScale);
        }
    }
}