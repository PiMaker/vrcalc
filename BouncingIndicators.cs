using UdonSharp;
using UnityEngine;

namespace pi.vrcalc
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class BouncingIndicators : UdonSharpBehaviour
    {
        private RectTransform[] _indicators;
        private float[] _startOffset;

        private void Start()
        {
            var childCount = transform.childCount;
            _indicators = new RectTransform[childCount];
            _startOffset = new float[childCount];
            for (int i = 0; i < childCount; i++)
            {
                _indicators[i] = transform.GetChild(i).GetComponent<RectTransform>();
                _startOffset[i] = _indicators[i].localPosition.y;
            }
        }

        private void Update()
        {
            var time = Time.time;
            for (int i = 0; i < _indicators.Length; i++)
            {
                var indicator = _indicators[i];
                var offset = Mathf.Sin(time * 5f + i * (Mathf.PI * 2f) / (_indicators.Length + 1)) * 0.5f;
                indicator.localPosition = new Vector3(indicator.localPosition.x, _startOffset[i] + offset * 8f, indicator.localPosition.z);
            }
        }
    }
}