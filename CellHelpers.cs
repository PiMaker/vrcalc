using System.Globalization;
using TMPro;
using UnityEngine;
using VRC.SDK3.Data;

namespace pi.vrcalc
{
    public static class CellHelpers
    {
        public static DataList GetCellDataBetween(CellHolder cellHolder, string pathA, string pathB)
        {
            var retval = new DataList();

            var cellsPath = cellHolder.CellsPath;
            var cellsRowCol = cellHolder.CellsRowCol;

            var cellA = cellsPath[pathA].DataDictionary;
            var cellB = cellsPath[pathB].DataDictionary;

            var rowA = cellA["row"].Int;
            var colNumA = cellA["colNum"].Int;
            var rowB = cellB["row"].Int;
            var colNumB = cellB["colNum"].Int;

            var minRow = Mathf.Min(rowA, rowB);
            var maxRow = Mathf.Max(rowA, rowB);
            var minColNum = Mathf.Min(colNumA, colNumB);
            var maxColNum = Mathf.Max(colNumA, colNumB);

            for (int row = minRow; row <= maxRow; row++)
            {
                for (int colNum = minColNum; colNum <= maxColNum; colNum++)
                {
                    var cellData = cellsRowCol[row].DataDictionary[colNum].DataDictionary;
                    retval.Add(cellData);
                }
            }

            return retval;
        }

        public static void ClearDependencies(CellHolder cellHolder, DataDictionary cellData)
        {
            var path = cellData["path"].String;
            var dependsOn = cellData["dependsOn"].DataList;

            var dependsOnCount = dependsOn.Count;
            for (int i = 0; i < dependsOnCount; i++)
            {
                var depPath = dependsOn[i].String;
                if (cellHolder.CellsPath.TryGetValue(depPath, out var depCellData))
                {
                    var depCellDict = depCellData.DataDictionary;
                    var dependedBy = depCellDict["dependedBy"].DataList;
                    dependedBy.Remove(path);
                }
            }

            dependsOn.Clear();
        }

        public static void AddDependency(CellHolder cellHolder, DataDictionary cellData, string depPath)
        {
            var path = cellData["path"].String;
            var dependsOn = cellData["dependsOn"].DataList;

            if (!dependsOn.Contains(depPath))
            {
                dependsOn.Add(depPath);
            }

            if (cellHolder.CellsPath.TryGetValue(depPath, out var depCellData))
            {
                var depCellDict = depCellData.DataDictionary;
                var dependedBy = depCellDict["dependedBy"].DataList;

                if (!dependedBy.Contains(path))
                {
                    dependedBy.Add(path);
                }
            }
        }

        public static string FormatDouble(double value, int maxLength)
        {
            for (int precision = 10 /* max 17 */; precision >= 1; precision--)
            {
                string s = value.ToString($"G{precision}", CultureInfo.InvariantCulture);
                if (s.Length <= maxLength)
                    return s;
            }

            string minimal = value.ToString("G1", CultureInfo.InvariantCulture);
            return minimal.Length <= maxLength
                ? minimal
                : minimal.Substring(0, maxLength);
        }
    }
}