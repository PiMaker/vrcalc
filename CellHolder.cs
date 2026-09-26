using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Data;

namespace pi.vrcalc
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class CellHolder : UdonSharpBehaviour
    {
        /*
            cellData:
            {
                "row": 1,
                "colNum": 1,
                "colLetter": "A",
                "path": "A1",
                "obj": (GameObject)root,
                "text": (Text)root.GetComponentInChildren<Text>(),
                "button": (Button)root.GetComponentInChildren<Button>(),
                "formulaIndicator": (GameObject)root.Find("FormulaIndicator"),

                "border1": typeof(Image), // Remote active
                "border2": typeof(Image), // Multi-select
                "border3": typeof(Image), // Active

                "overlay1": typeof(Image), // Remote active
                "overlay2": typeof(Image), // Multi-select
                "overlay3": typeof(Image), // Active

                "usernamesObj": root.Find("Usernames").gameObject,
                "usernamesText": root.Find("Usernames/UsernamesTest").GetComponent<TextMeshProUGUI>(),
                "usernamesSelected": DataList<string>, // usernames of *remote* players who have this cell selected

                "formula": "=A1+B1", // string, can be empty
                "updatedAt": typeof(long), // when this cell last had a formula update, typeof(long) server-time .Ticks, starts as long.MinValue

                "executionGraph": typeof(DataDictionary), // tree representation of parsed formula, starts as null
                "value": typeof(string), // evaluated value of the formula, starts as empty string

                "dependsOn": DataList<string>, // paths of cells this cell depends on, starts as empty list
                "dependedBy": DataList<string>, // paths of cells that depend on this cell, starts as empty list
                "fullDepList": DataDictionary<string, bool> (HashSet), // paths of all calls this cells depends on, including transitively, only for recursion check

                "style": ulong, // formatting options, bitflags and color, starts as 0UL
                "styleUpdatedAt": typeof(long), // when this cell last had a style update, typeof(long) server-time .Ticks, starts as long.MinValue
            }
        */

        [SerializeField] private DebugLogger _log;

        public DataDictionary CellsRowCol { get; private set; } // row -> col -> cellData
        public DataDictionary CellsColRow { get; private set; } // col -> row -> cellData
        public DataDictionary CellsPath { get; private set; } // path ("A1") -> cellData

        public string MinColumnLetter { get; private set; }
        public string MaxColumnLetter { get; private set; }
        public int MinColumnNumber { get; private set; }
        public int MaxColumnNumber { get; private set; }
        public int MinRow { get; private set; }
        public int MaxRow { get; private set; }

        [Space]
        [SerializeField] private Transform _rowsRoot;
        [SerializeField] private Transform _rowLabelsRoot;
        [SerializeField] private Transform _columnLabelsRoot;

        [Space]
        [SerializeField] private Transform _colorButtonsRoot;

        private void Start()
        {
            CellsRowCol = new DataDictionary();
            CellsColRow = new DataDictionary();
            CellsPath = new DataDictionary();

            var rowTransforms = _rowsRoot.childCount;
            var colTransforms = _rowsRoot.GetChild(0).childCount;

            MinColumnLetter = "Z";
            MaxColumnLetter = "A";
            MinColumnNumber = 9999;
            MaxColumnNumber = 0;
            MinRow = 9999;
            MaxRow = 0;

            for (int row = 1; row <= rowTransforms; row++)
            {
                var rowTransform = _rowsRoot.Find(row.ToString());
                if (rowTransform == null)
                    continue;

                rowTransform.GetComponent<HorizontalLayoutGroup>().enabled = false;

                for (int col = 1; col <= colTransforms; col++)
                {
                    var colLetter = ((char)('A' + col - 1)).ToString();
                    var cellTransform = rowTransform.Find(colLetter);
                    if (cellTransform == null)
                        continue;

                    var cellPath = $"{colLetter}{row}";
                    var cellObj = cellTransform.gameObject;

                    var cellData = new DataDictionary();
                    cellData["row"] = row;
                    cellData["colNum"] = col;
                    cellData["colLetter"] = colLetter;
                    cellData["path"] = cellPath;
                    cellData["obj"] = cellObj;
                    cellData["text"] = cellObj.GetComponentInChildren<Text>();
                    cellData["button"] = cellObj.GetComponentInChildren<Button>();
                    cellData["formulaIndicator"] = cellTransform.Find("FormulaIndicator").gameObject;

                    cellData["formula"] = "";
                    cellData["updatedAt"] = long.MinValue;

                    cellData["border1"] = cellTransform.Find("Border1").GetComponent<Image>();
                    cellData["border2"] = cellTransform.Find("Border2").GetComponent<Image>();
                    cellData["border3"] = cellTransform.Find("Border3").GetComponent<Image>();
                    cellData["overlay1"] = cellTransform.Find("Overlay1").GetComponent<Image>();
                    cellData["overlay2"] = cellTransform.Find("Overlay2").GetComponent<Image>();
                    cellData["overlay3"] = cellTransform.Find("Overlay3").GetComponent<Image>();

                    cellData["usernamesObj"] = cellTransform.Find("Usernames").gameObject;
                    cellData["usernamesText"] = cellTransform.Find("Usernames/UsernamesText").GetComponent<TextMeshProUGUI>();
                    cellData["usernamesSelected"] = new DataList();

                    cellData["executionGraph"] = new DataToken();
                    cellData["value"] = "";

                    cellData["dependsOn"] = new DataList();
                    cellData["dependedBy"] = new DataList();
                    cellData["fullDepList"] = new DataDictionary();

                    cellData["style"] = 0UL;
                    cellData["styleUpdatedAt"] = long.MinValue;

                    CellsPath[cellPath] = cellData;
                    if (!CellsRowCol.ContainsKey(row))
                        CellsRowCol[row] = new DataDictionary();
                    CellsRowCol[row].DataDictionary[col] = cellData;
                    if (!CellsColRow.ContainsKey(col))
                        CellsColRow[col] = new DataDictionary();
                    CellsColRow[col].DataDictionary[row] = cellData;

                    if (colLetter.CompareTo(MinColumnLetter) < 0)
                        MinColumnLetter = colLetter;
                    if (colLetter.CompareTo(MaxColumnLetter) > 0)
                        MaxColumnLetter = colLetter;
                    if (col < MinColumnNumber)
                        MinColumnNumber = col;
                    if (col > MaxColumnNumber)
                        MaxColumnNumber = col;
                    if (row < MinRow)
                        MinRow = row;
                    if (row > MaxRow)
                        MaxRow = row;
                }
            }

            // disable on load for performance
            _rowsRoot.GetComponent<VerticalLayoutGroup>().enabled = false;
            _rowLabelsRoot.GetComponent<VerticalLayoutGroup>().enabled = false;
            _columnLabelsRoot.GetComponent<HorizontalLayoutGroup>().enabled = false;

            InitColorButtons();

            _log.Log($"CellHolder initialized with {CellsPath.Count} cells: {MinColumnLetter}{MinRow} to {MaxColumnLetter}{MaxRow}");
        }

        public Color[] ColorPalette = new Color[16];

        private void InitColorButtons()
        {
            for (int row = 0; row < _colorButtonsRoot.childCount; row++)
            {
                var rowTransform = _colorButtonsRoot.GetChild(row);
                var rowChildCount = rowTransform.childCount;
                for (int col = 0; col < rowChildCount; col++)
                {
                    var buttonTransform = rowTransform.GetChild(col);
                    var image = buttonTransform.GetComponent<Image>();
                    image.color = ColorPalette[row * rowChildCount + col];
                }
            }
        }
    }
}