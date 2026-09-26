using System;
using TMPro;
using UdonSharp;
using UnityEngine;

namespace pi.vrcalc
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class DebugLogger : UdonSharpBehaviour
    {
        [SerializeField] private TextMeshProUGUI _logText;
        [SerializeField] private int _maxLines = 20;

        public void Log(string message)
        {
            Debug.Log($"[VRCCalc] {message}");
            var currentText = _logText.text;
            var newLine = $"[{DateTime.Now}] {message}";
            var lines = currentText.Split('\n');
            if (lines.Length >= _maxLines)
                currentText = string.Join("\n", lines, 0, lines.Length - 1);
            _logText.text = $"{newLine}\n{currentText}";
        }
    }
}