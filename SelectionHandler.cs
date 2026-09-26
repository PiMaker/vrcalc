using System.Text;
using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Data;

namespace pi.vrcalc
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SelectionHandler : UdonSharpBehaviour
    {
        [SerializeField] private CellHolder _cellHolder;
        [SerializeField] private Synchronizer _synchronizer;
        [SerializeField] private PerPlayerHolder _perPlayerHolder;
        [SerializeField] private TMP_InputField _formulaField;
        [SerializeField] private TextMeshProUGUI _debugOutput;
        [SerializeField] private TextMeshProUGUI _currentCellText;

        private DataDictionary _activeCell;
        private DataList _selectedCells;

        private StringBuilder _debugSB;

        private void Start()
        {
            _selectedCells = new DataList();
            _debugSB = new StringBuilder();

            SendCustomEventDelayedFrames(nameof(_Init), 1);
        }

        public void _Init()
        {
            Activate("A1");
            RefreshDebugOutput();
        }

        public void HandleButton(string path, bool allowMultiSelect)
        {
            if (allowMultiSelect && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
            {
                RemoveMultiSelect();
                var betweenData = CellHelpers.GetCellDataBetween(_cellHolder, _activeCell["path"].String, path);
                SelectMultiple(betweenData);
            }
            else
            {
                RemoveMultiSelect();
                Activate(path);
            }
        }

        public void HandleButtonDoubleClick(string path)
        {
            // the first click as set it as _activeCell, so just trust that and open the formula field for editing
            var button = _formulaField.GetComponentInChildren<Button>();
            if (button != null)
            {
                button.OnSubmit(null);
            }
        }

        public void HandleButtonMultiSelectEnter(string path)
        {
            RemoveMultiSelect();
            var betweenData = CellHelpers.GetCellDataBetween(_cellHolder, _activeCell["path"].String, path);
            SelectMultiple(betweenData);
        }

        public void HandleRowButton(int row)
        {
            RemoveMultiSelect();
            var rowData = CellHelpers.GetCellDataBetween(_cellHolder, $"{_cellHolder.MinColumnLetter}{row}", $"{_cellHolder.MaxColumnLetter}{row}");
            SelectMultiple(rowData);
            Activate(rowData[0].DataDictionary["path"].String);
        }

        public void HandleColButton(string col)
        {
            RemoveMultiSelect();
            var colData = CellHelpers.GetCellDataBetween(_cellHolder, $"{col}{_cellHolder.MinRow}", $"{col}{_cellHolder.MaxRow}");
            SelectMultiple(colData);
            Activate(colData[0].DataDictionary["path"].String);
        }

        private DataList _copyQueue;
        public void HandleCopyButton()
        {
            if (_copyQueue == null)
                _copyQueue = new DataList();
            
            var needsStartQueue = _copyQueue.Count == 0;

            var activeFormula = _activeCell["formula"].String;
            _copyQueue.Add(activeFormula);

            var selectedCount = _selectedCells.Count;
            for (int i = 0; i < selectedCount; i++)
            {
                var cellData = _selectedCells[i].DataDictionary;
                _copyQueue.Add(cellData);
            }

            if (needsStartQueue)
            {
                SendCustomEventDelayedFrames(nameof(RunCopyQueue), 1);
            }
        }

        private string _copySourceFormula;
        public void RunCopyQueue()
        {
            var queueCount = _copyQueue.Count;
            if (queueCount == 0)
                return;

            var entry = _copyQueue[0];
            _copyQueue.RemoveAt(0);

            if (entry.TokenType == TokenType.String)
            {
                _copySourceFormula = entry.String;
            }
            else
            {
                var cellData = entry.DataDictionary;
                var path = cellData["path"].String;
                _synchronizer.SetCellFormula(path, _copySourceFormula);
            }

            if (_copyQueue.Count > 0)
                SendCustomEventDelayedFrames(nameof(RunCopyQueue), 1);

            RefreshDebugOutput();
        }

        private void Activate(string path)
        {
            if (_activeCell != null)
            {
                var oldBorder = (Image)_activeCell["border3"].Reference;
                oldBorder.enabled = false;
                var oldOverlay = (Image)_activeCell["overlay3"].Reference;
                oldOverlay.enabled = false;

                if (_selectedCells.Contains(_activeCell))
                {
                    var oldBorder2 = (Image)_activeCell["border2"].Reference;
                    oldBorder2.enabled = true;
                    var oldOverlay2 = (Image)_activeCell["overlay2"].Reference;
                    oldOverlay2.enabled = true;
                }
            }

            var cellData = _cellHolder.CellsPath[path].DataDictionary;
            _activeCell = cellData;
            var border = (Image)cellData["border3"].Reference;
            border.enabled = true;
            var overlay = (Image)cellData["overlay3"].Reference;
            overlay.enabled = true;

            var perPlayerHandler = _perPlayerHolder.GetHandlerForLocalPlayer();
            if (perPlayerHandler != null)
            {
                perPlayerHandler.SetActiveCell(path);
            }
            else
            {
                SendCustomEventDelayedFrames(nameof(SetActiveCellTryAgain), 5);
            }

            RefreshFormulaText();
            RefreshDebugOutput();
        }

        public void SetActiveCellTryAgain()
        {
            if (_activeCell != null)
            {
                var path = _activeCell["path"].String;
                var perPlayerHandler = _perPlayerHolder.GetHandlerForLocalPlayer();
                if (perPlayerHandler != null)
                {
                    perPlayerHandler.SetActiveCell(path);
                }
                else
                {
                    SendCustomEventDelayedFrames(nameof(SetActiveCellTryAgain), 5);
                }
            }
        }

        private void SelectMultiple(DataList cellDataList)
        {
            for (int i = 0; i < cellDataList.Count; i++)
            {
                var cellData = cellDataList[i].DataDictionary;
                _selectedCells.Add(cellData);
                if (cellData != _activeCell)
                {
                    var border = (Image)cellData["border2"].Reference;
                    border.enabled = true;
                    var overlay = (Image)cellData["overlay2"].Reference;
                    overlay.enabled = true;
                }
            }

            RefreshDebugOutput();
        }

        private void RefreshFormulaText()
        {
            _formulaField.SetTextWithoutNotify(_activeCell["formula"].String);
        }

        public void HandleCellValueChanged(string path)
        {
            if (path == _activeCell["path"].String)
            {
                RefreshFormulaText();
                RefreshDebugOutput();
            }
        }

        public void HandleFormulaFieldChanged()
        {
            _synchronizer.SetCellFormula(_activeCell["path"].String, _formulaField.text);
        }

        private void RemoveMultiSelect()
        {
            for (int i = 0; i < _selectedCells.Count; i++)
            {
                var cellData = _selectedCells[i].DataDictionary;
                var border = (Image)cellData["border2"].Reference;
                border.enabled = false;
                var overlay = (Image)cellData["overlay2"].Reference;
                overlay.enabled = false;
            }

            _selectedCells.Clear();
        }

        public DataDictionary GetActiveCell()
        {
            return _activeCell;
        }

        public DataList GetSelectedCells()
        {
            return _selectedCells;
        }

        private void RefreshDebugOutput()
        {
            _currentCellText.text = _activeCell["path"].String;

            _debugSB.Clear();
            _debugSB.AppendFormat("Copy Queue: {0}\n", _copyQueue != null ? _copyQueue.Count : 0);

            if (_selectedCells.Count > 0)
            {
                var multiTopLeft = _selectedCells[0].DataDictionary;
                var multiBottomRight = _selectedCells[0].DataDictionary;
                var multiTopLeftRow = multiTopLeft["row"].Int;
                var multiTopLeftColNum = multiTopLeft["colNum"].Int;
                var multiBottomRightRow = multiBottomRight["row"].Int;
                var multiBottomRightColNum = multiBottomRight["colNum"].Int;
                for (int i = 1; i < _selectedCells.Count; i++)
                {
                    var cellData = _selectedCells[i].DataDictionary;
                    if (cellData["row"].Int < multiTopLeftRow || cellData["colNum"].Int < multiTopLeftColNum)
                    {
                        multiTopLeft = cellData;
                        multiTopLeftRow = cellData["row"].Int;
                        multiTopLeftColNum = cellData["colNum"].Int;
                    }
                    if (cellData["row"].Int > multiBottomRightRow || cellData["colNum"].Int > multiBottomRightColNum)
                    {
                        multiBottomRight = cellData;
                        multiBottomRightRow = cellData["row"].Int;
                        multiBottomRightColNum = cellData["colNum"].Int;
                    }
                }
                _debugSB.AppendFormat("Multi-Selected Cells: {0} - {1}:{2}\n", _selectedCells.Count, multiTopLeft["path"].String, multiBottomRight["path"].String);
            }
            else
            {
                _debugSB.AppendLine("Multi-Selected Cells: 0");
            }

            _debugSB.AppendLine("Active Cell:");
            _debugSB.AppendFormat("  Path: {0} (col {1}, row {2})\n", _activeCell["path"].String, _activeCell["colNum"].Int, _activeCell["row"].Int);
            _debugSB.AppendFormat("  Formula: {0}\n", _activeCell["formula"].String);
            _debugSB.AppendFormat("  Displayed Text: {0}\n", ((Text)_activeCell["text"].Reference).text);
            _debugSB.AppendFormat("  Cached Value: {0}\n", _activeCell["value"].ToString());
            _debugSB.AppendFormat("  UpdatedAt: {0}\n", _activeCell["updatedAt"].Long);

            var border1 = (Image)_activeCell["border1"].Reference;
            var border2 = (Image)_activeCell["border2"].Reference;
            var border3 = (Image)_activeCell["border3"].Reference;
            var overlay1 = (Image)_activeCell["overlay1"].Reference;
            var overlay2 = (Image)_activeCell["overlay2"].Reference;
            var overlay3 = (Image)_activeCell["overlay3"].Reference;
            _debugSB.AppendFormat("  Border: {0}, {1}, {2}\n", border1.enabled, border2.enabled, border3.enabled);
            _debugSB.AppendFormat("  Overlay: {0}, {1}, {2}", overlay1.enabled, overlay2.enabled, overlay3.enabled);

            var dependsOn = _activeCell["dependsOn"].DataList;
            var dependsOnCount = dependsOn.Count;
            if (dependsOnCount > 0)
            {
                _debugSB.Append("\n  Depends On: ");
                _debugSB.Append(dependsOn[0].String);
                for (int i = 1; i < dependsOnCount; i++)
                {
                    _debugSB.Append(", ");
                    _debugSB.Append(dependsOn[i].String);
                }
            }

            var dependedBy = _activeCell["dependedBy"].DataList;
            var dependedByCount = dependedBy.Count;
            if (dependedByCount > 0)
            {
                _debugSB.Append("\n  Depended By: ");
                _debugSB.Append(dependedBy[0].String);
                for (int i = 1; i < dependedByCount; i++)
                {
                    _debugSB.Append(", ");
                    _debugSB.Append(dependedBy[i].String);
                }
            }

            var fullDepList = _activeCell["fullDepList"].DataDictionary;
            var fullDepListCount = fullDepList.Count;
            if (fullDepListCount > 0)
            {
                _debugSB.Append("\n  Full Dependency List: ");
                var fullDepListKeys = fullDepList.GetKeys();
                _debugSB.Append(fullDepListKeys[0]);
                for (int i = 1; i < fullDepListCount; i++)
                {
                    _debugSB.Append(", ");
                    _debugSB.Append(fullDepListKeys[i]);
                }
            }

            _debugOutput.text = _debugSB.ToString();
        }
    }
}