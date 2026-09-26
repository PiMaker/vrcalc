using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Data;

namespace pi.vrcalc
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class StyleHandler : UdonSharpBehaviour
    {
        [SerializeField] private DebugLogger _log;
        [SerializeField] private CellHolder _cellHolder;
        [SerializeField] private Synchronizer _synchronizer;
        [SerializeField] private SelectionHandler _selectionHandler;
        [SerializeField] private GameObject _colorModal;

        private const ulong StyleBold = 1 << 0;
        private const ulong StyleItalic = 1 << 1;
        private const ulong StyleColorMask = 0xF << 2; // 4 bits for color, 0-15

        public void RefreshStyle(DataDictionary cellData)
        {
            var text = (Text)cellData["text"].Reference;
            var style = cellData["style"].ULong;

            var fontStyle = style & 0x3;
            switch (fontStyle)
            {
                case 0:
                    text.fontStyle = FontStyle.Normal;
                    break;
                case StyleBold:
                    text.fontStyle = FontStyle.Bold;
                    break;
                case StyleItalic:
                    text.fontStyle = FontStyle.Italic;
                    break;
                case StyleBold | StyleItalic:
                    text.fontStyle = FontStyle.BoldAndItalic;
                    break;
            }

            var colorIndex = (int)((style & StyleColorMask) >> 2);
            var color = _cellHolder.ColorPalette[colorIndex];
            text.color = color;
        }

        public void HandleBoldButton()
        {
            FlipBit(StyleBold);
        }

        public void HandleItalicButton()
        {
            FlipBit(StyleItalic);
        }

        public void HandleColorModalButton()
        {
            _colorModal.SetActive(!_colorModal.activeSelf);
        }

        public void HandleColorButton(int index)
        {
            index = Mathf.Clamp(index, 0, 15);
            var selected = _selectionHandler.GetSelectedCells();
            if (selected.Count == 0)
            {
                var active = _selectionHandler.GetActiveCell();
                SetColor(active, index);
            }
            else
            {
                var selectedCount = selected.Count;
                for (int i = 0; i < selectedCount; i++)
                {
                    var cellData = selected[i].DataDictionary;
                    SetColor(cellData, index);
                }
            }
            _colorModal.SetActive(false);
        }

        private void SetColor(DataDictionary cellData, int index)
        {
            var path = cellData["path"].String;
            var style = cellData["style"].ULong;
            style &= ~StyleColorMask; // Clear the color bits
            style |= (ulong)index << 2; // Set the new color index
            _synchronizer.SetCellStyle(path, style);
        }

        private void FlipBit(ulong bit)
        {
            var selected = _selectionHandler.GetSelectedCells();
            if (selected.Count == 0)
            {
                var active = _selectionHandler.GetActiveCell();
                var path = active["path"].String;
                var style = active["style"].ULong;
                style ^= bit;
                _synchronizer.SetCellStyle(path, style);
            }
            else
            {
                var selectedCount = selected.Count;
                for (int i = 0; i < selectedCount; i++)
                {
                    var cellData = selected[i].DataDictionary;
                    var path = cellData["path"].String;
                    var style = cellData["style"].ULong;
                    style ^= bit;
                    _synchronizer.SetCellStyle(path, style);
                }
            }
        }
    }
}