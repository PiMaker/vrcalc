using System.Text;
using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Data;
using VRC.SDKBase;

namespace pi.vrcalc
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ImportExportHandler : UdonSharpBehaviour
    {
        [SerializeField] private DebugLogger _log;
        [SerializeField] private CellHolder _cellHolder;
        [SerializeField] private SelectionHandler _selectionHandler;
        [SerializeField] private Synchronizer _synchronizer;

        [Space]
        [SerializeField] private TMP_InputField _inputField;
        [SerializeField] private TMP_InputField _delimiterField;
        [SerializeField] private Toggle _exportFormulaToggle;
        [SerializeField] private Toggle _selectionToggle;

        // Hacky :3
        private void Start()
        {
            SendCustomEventDelayedFrames(nameof(DelayedStart), 2);
        }

        public void DelayedStart()
        {
            if (Networking.LocalPlayer.isMaster && VRCPlayerApi.GetPlayerCount() == 1)
            {
                _log.Log("ImportExportHandler: Initializing with default content.");
                HandleImportButton();
            }
        }

        public void HandleImportButton()
        {
            var delim = _delimiterField.text;
            if (delim.Length == 0)
            {
                _log.Log("Import: Delimiter cannot be empty.");
                //_inputField.text = "Import: Delimiter cannot be empty.";
                return;
            }

            var minColNum = int.MaxValue;
            var maxColNum = int.MinValue;
            var minRow = int.MaxValue;
            var maxRow = int.MinValue;

            if (_selectionToggle.isOn)
            {
                var selectedCells = _selectionHandler.GetSelectedCells();
                var selCount = selectedCells.Count;

                if (selCount == 0)
                {
                    var activeCell = _selectionHandler.GetActiveCell();
                    minRow = activeCell["row"].Int;
                    minColNum = activeCell["colNum"].Int;
                    maxRow = int.MaxValue;
                    maxColNum = int.MaxValue;
                }
                else
                {
                    for (int i = 0; i < selCount; i++)
                    {
                        var cellData = selectedCells[i].DataDictionary;
                        var row = cellData["row"].Int;
                        var colNum = cellData["colNum"].Int;

                        minRow = Mathf.Min(minRow, row);
                        maxRow = Mathf.Max(maxRow, row);
                        minColNum = Mathf.Min(minColNum, colNum);
                        maxColNum = Mathf.Max(maxColNum, colNum);
                    }
                }
            }
            else
            {
                minColNum = _cellHolder.MinColumnNumber;
                maxColNum = _cellHolder.MaxColumnNumber;
                minRow = _cellHolder.MinRow;
                maxRow = _cellHolder.MaxRow;
            }

            var inputText = _inputField.text.Replace("\r", "");
            var lines = inputText.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            var rowCount = lines.Length;
            var delimArr = new[] { delim };

            rowCount = Mathf.Min(rowCount, maxRow - minRow + 1);
            for (int rowOffset = 0; rowOffset < rowCount; rowOffset++)
            {
                var line = lines[rowOffset];
                var values = line.Split(delimArr, System.StringSplitOptions.None);
                var colCount = values.Length;

                colCount = Mathf.Min(colCount, maxColNum - minColNum + 1);
                for (int colOffset = 0; colOffset < colCount; colOffset++)
                {
                    var value = values[colOffset];
                    var row = minRow + rowOffset;
                    var colNum = minColNum + colOffset;

                    if (_cellHolder.CellsRowCol.TryGetValue(row, out var rowDict) &&
                        rowDict.DataDictionary.TryGetValue(colNum, out var cellData))
                    {
                        _synchronizer.SetCellFormula(cellData.DataDictionary["path"].String, value);
                    }
                }
            }

            _log.Log($"Import: {rowCount} rows imported.");
        }

        public void HandleExportButton()
        {
            var delim = _delimiterField.text;
            if (delim.Length == 0)
            {
                _log.Log("Export: Delimiter cannot be empty.");
                _inputField.text = "Export: Delimiter cannot be empty.";
                return;
            }

            var sb = new StringBuilder();

            var minColNum = int.MaxValue;
            var maxColNum = int.MinValue;
            var minRow = int.MaxValue;
            var maxRow = int.MinValue;

            if (_selectionToggle.isOn)
            {
                var selectedCells = _selectionHandler.GetSelectedCells();
                var selCount = selectedCells.Count;

                if (selCount == 0)
                {
                    var activeCell = _selectionHandler.GetActiveCell();
                    _inputField.text = GetExportValue(activeCell);
                    return;
                }

                for (int i = 0; i < selCount; i++)
                {
                    var cellData = selectedCells[i].DataDictionary;
                    var row = cellData["row"].Int;
                    var colNum = cellData["colNum"].Int;

                    minRow = Mathf.Min(minRow, row);
                    maxRow = Mathf.Max(maxRow, row);
                    minColNum = Mathf.Min(minColNum, colNum);
                    maxColNum = Mathf.Max(maxColNum, colNum);
                }
            }
            else
            {
                minColNum = _cellHolder.MinColumnNumber;
                maxColNum = _cellHolder.MaxColumnNumber;
                minRow = _cellHolder.MinRow;
                maxRow = _cellHolder.MaxRow;
            }

            for (int row = minRow; row <= maxRow; row++)
            {
                for (int colNum = minColNum; colNum <= maxColNum; colNum++)
                {
                    if (_cellHolder.CellsRowCol.TryGetValue(row, out var rowDict) &&
                        rowDict.DataDictionary.TryGetValue(colNum, out var cellData))
                    {
                        sb.Append(GetExportValue(cellData.DataDictionary));
                    }

                    if (colNum < maxColNum)
                    {
                        sb.Append(delim);
                    }
                }

                if (row < maxRow)
                {
                    sb.AppendLine();
                }
            }

            _inputField.text = sb.ToString();
            _log.Log($"Export: {maxRow - minRow + 1} rows exported.");
        }

        private string GetExportValue(DataDictionary cellData)
        {
            if (_exportFormulaToggle.isOn)
            {
                return cellData["formula"].String;
            }
            else
            {
                var value = cellData["value"].String;
                return string.IsNullOrEmpty(value) ? cellData["formula"].String : value;
            }
        }
    }
}