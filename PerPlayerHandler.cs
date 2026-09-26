using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;

namespace pi.vrcalc
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class PerPlayerHandler : UdonSharpBehaviour
    {
        [SerializeField] private CellHolder _cellHolder;
        [SerializeField] private Synchronizer _synchronizer;
        [SerializeField] private DebugLogger _log;

        [UdonSynced] private string _activeCell;
        private string _prevActivatedCell;

        [NetworkCallable(maxEventsPerSecond: 100)]
        public void RPC_SetCellFormula_TargetedMulti(string[] paths, string[] formulas, long[] updatedAts)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                var path = paths[i];
                var formula = formulas[i];
                var updatedAt = updatedAts[i];

                _synchronizer.RPC_SetCellFormula(path, formula, updatedAt);
            }
        }

        [NetworkCallable(maxEventsPerSecond: 100)]
        public void RPC_SetCellStyle_TargetedMulti(string[] paths, ulong[] styles, long[] updatedAts)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                var path = paths[i];
                var style = styles[i];
                var updatedAt = updatedAts[i];

                _synchronizer.RPC_SetCellStyle(path, style, updatedAt);
            }
        }

        public void SetActiveCell(string path)
        {
            _activeCell = path;
            RequestSerialization();
        }

        public override void OnDeserialization()
        {
            if (!Networking.IsOwner(gameObject))
            {
                if (!string.IsNullOrEmpty(_activeCell))
                {
                    if (_cellHolder.CellsPath.TryGetValue(_activeCell, out var cellData))
                    {
                        var owner = Networking.GetOwner(gameObject);
                        var ownerName = owner.displayName;

                        if (!string.IsNullOrEmpty(_prevActivatedCell) && _cellHolder.CellsPath.TryGetValue(_prevActivatedCell, out var prevCellData))
                        {
                            var prevDict = prevCellData.DataDictionary;
                            var prevSelected = prevDict["usernamesSelected"].DataList;
                            prevSelected.Remove(ownerName); // unique, at least in theory
                            if (prevSelected.Count == 0)
                            {
                                var prevBorder1 = (Image)prevDict["border1"].Reference;
                                prevBorder1.enabled = false;
                                var prevOverlay1 = (Image)prevDict["overlay1"].Reference;
                                prevOverlay1.enabled = false;
                                var prevUsernamesObj = (GameObject)prevDict["usernamesObj"].Reference;
                                prevUsernamesObj.SetActive(false);
                            }
                            else
                            {
                                var prevUsernamesText = (TextMeshProUGUI)prevDict["usernamesText"].Reference;
                                prevUsernamesText.text = "";
                                for (int i = 0; i < prevSelected.Count; i++)
                                {
                                    if (i > 0)
                                        prevUsernamesText.text += ", ";
                                    prevUsernamesText.text += prevSelected[i].String;
                                }
                            }
                        }

                        var dict = cellData.DataDictionary;
                        var selected = dict["usernamesSelected"].DataList;
                        selected.Add(ownerName);
                        if (selected.Count == 1)
                        {
                            var border1 = (Image)dict["border1"].Reference;
                            border1.enabled = true;
                            var overlay1 = (Image)dict["overlay1"].Reference;
                            overlay1.enabled = true;

                            var usernamesObj = (GameObject)dict["usernamesObj"].Reference;
                            usernamesObj.SetActive(true);
                        }

                        var usernamesText = (TextMeshProUGUI)dict["usernamesText"].Reference;
                        usernamesText.text = "";
                        for (int i = 0; i < selected.Count; i++)
                        {
                            if (i > 0)
                                usernamesText.text += ", ";
                            usernamesText.text += selected[i].String;
                        }
                    }
                }

                _prevActivatedCell = _activeCell;
            }
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (Networking.IsOwner(player, gameObject))
            {
                // player left, clear their cell selection
                var ownerName = player.displayName;

                if (!string.IsNullOrEmpty(_prevActivatedCell) && _cellHolder.CellsPath.TryGetValue(_prevActivatedCell, out var prevCellData))
                {
                    _log.Log($"Player {ownerName} left, clearing their cell selection: {_prevActivatedCell}");

                    var prevDict = prevCellData.DataDictionary;
                    var prevSelected = prevDict["usernamesSelected"].DataList;
                    prevSelected.Remove(ownerName); // unique, at least in theory
                    if (prevSelected.Count == 0)
                    {
                        var prevBorder1 = (Image)prevDict["border1"].Reference;
                        prevBorder1.enabled = false;
                        var prevOverlay1 = (Image)prevDict["overlay1"].Reference;
                        prevOverlay1.enabled = false;
                        var prevUsernamesObj = (GameObject)prevDict["usernamesObj"].Reference;
                        prevUsernamesObj.SetActive(false);
                    }
                    else
                    {
                        var prevUsernamesText = (TextMeshProUGUI)prevDict["usernamesText"].Reference;
                        prevUsernamesText.text = "";
                        for (int i = 0; i < prevSelected.Count; i++)
                        {
                            if (i > 0)
                                prevUsernamesText.text += ", ";
                            prevUsernamesText.text += prevSelected[i].String;
                        }
                    }
                }

                _prevActivatedCell = null;
                _activeCell = null;
            }
        }
    }
}