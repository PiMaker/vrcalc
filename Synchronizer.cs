using System;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;

namespace pi.vrcalc
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class Synchronizer : UdonSharpBehaviour
    {
        [SerializeField] private DebugLogger _log;
        [SerializeField] private CellHolder _cellHolder;
        [SerializeField] private CellEvaluator _cellEvaluator;
        [SerializeField] private PerPlayerHolder _perPlayerHolder;
        [SerializeField] private GameObject _loadingOverlay;

        [SerializeField] private SelectionHandler _selectionHandler;
        [SerializeField] private StyleHandler _styleHandler;

        private void Start()
        {
            if (!Networking.IsOwner(gameObject))
            {
                _loadingOverlay.SetActive(true);
                SendCustomEventDelayedSeconds(nameof(LocalStartupLoop), 1f);
            }
            else
            {
                _log.Log($"Skipping startup sync (local owner)");
                RPC_SetDone(Networking.LocalPlayer.playerId);
            }
        }

        private int _lastRequestedOwner = -1;
        private bool _syncDone = false;
        private bool _hasHandedOffOwnership = false;
        public void LocalStartupLoop()
        {
            if (_syncDone)
                return;

            var owner = Networking.GetOwner(gameObject);
            if (!owner.isLocal)
            {
                // request a sync from the owner if it changed
                if (owner.playerId != _lastRequestedOwner)
                {
                    _lastRequestedOwner = owner.playerId;
                    _log.Log($"Requesting startup sync from owner {owner.displayName} ({owner.playerId})");
                    NetworkCalling.SendCustomNetworkEvent(this, VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner,
                        nameof(RPC_RequestSync), Networking.LocalPlayer.playerId);
                }

                // re-check after 5 seconds until we're done, in case current owner leaves before finishing
                SendCustomEventDelayedSeconds(nameof(LocalStartupLoop), 5f);
            }
            else if (VRCPlayerApi.GetPlayerCount() > 1 && !_hasHandedOffOwnership)
            {
                // we became the owner at some point, but there are other players in the world - they might have data still?
                var players = VRCPlayerApi.GetPlayers();
                var nextInLine = players[0];
                if (nextInLine.isLocal)
                    nextInLine = players[1];
                _hasHandedOffOwnership = true;
                Networking.SetOwner(nextInLine, gameObject);
                _log.Log($"Startup sync became owner but wasn't ready, transferring ownership to {nextInLine.displayName} ({nextInLine.playerId})");
                SendCustomEventDelayedSeconds(nameof(LocalStartupLoop), 5f);
            }
            else
            {
                _log.Log($"Startup sync ended unsuccessfully ({(_hasHandedOffOwnership ? "looping ownership transfer" : "only we remain")})");
                _loadingOverlay.SetActive(false);
                _syncDone = true;
            }
        }

        [NetworkCallable(maxEventsPerSecond: 5)]
        public void RPC_RequestSync(int forPlayerId)
        {
            if (forPlayerId != Networking.LocalPlayer.playerId)
            {
                var perPlayerHandler = _perPlayerHolder.GetHandlerForPlayer(forPlayerId);
                if (perPlayerHandler == null)
                {
                    _log.Log($"No PerPlayerHandler found for player {forPlayerId}, cannot sync");
                    return;
                }

                _log.Log($"Sending sync to player {forPlayerId}");

                var rows = _cellHolder.CellsRowCol.Count;
                var cols = _cellHolder.CellsColRow.Count;
                var rowPaths = new string[cols];
                var rowFormulas = new string[cols];
                var rowUpdatedAts = new long[cols];
                var rowStyles = new ulong[cols];
                var rowStyleUpdatedAts = new long[cols];
                int sizeCounter = 5 * 4; // array overhead estimate
                const int maxSize = 16 * 1024; // 16 kB as per vrchat docs
                for (int row = 1; row <= rows; row++)
                {
                    int index = 0;
                    for (int colNum = 1; colNum <= cols; colNum++)
                    {
                        var cellData = _cellHolder.CellsRowCol[row].DataDictionary[colNum].DataDictionary;
                        var updatedAt = cellData["updatedAt"].Long;
                        var styleUpdatedAt = cellData["styleUpdatedAt"].Long;

                        if (updatedAt == long.MinValue && styleUpdatedAt == long.MinValue)
                            continue; // no data to sync

                        var path = cellData["path"].String;
                        var formula = cellData["formula"].String;
                        var style = cellData["style"].ULong;

                        sizeCounter += path.Length + formula.Length + 8 + 2*4 /* string headers */ + 8*2 /* style */;
                        if (sizeCounter > maxSize)
                        {
                            if (index == 0)
                            {
                                _log.Log($"Cell {path} exceeds max sync size ({sizeCounter} bytes), skipping");
                                continue;
                            }

                            _log.Log($"Reached max sync size ({sizeCounter} bytes) at row {row}, sending partial sync of {index} cells");
                            var rowPathsPartial = new string[index];
                            var rowFormulasPartial = new string[index];
                            var rowUpdatedAtsPartial = new long[index];
                            var rowStylesPartial = new ulong[index];
                            var rowStyleUpdatedAtsPartial = new long[index];
                            Array.Copy(rowPaths, rowPathsPartial, index);
                            Array.Copy(rowFormulas, rowFormulasPartial, index);
                            Array.Copy(rowUpdatedAts, rowUpdatedAtsPartial, index);
                            Array.Copy(rowStyles, rowStylesPartial, index);
                            Array.Copy(rowStyleUpdatedAts, rowStyleUpdatedAtsPartial, index);
                            NetworkCalling.SendCustomNetworkEvent(perPlayerHandler, VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner,
                                "RPC_SetCellFormula_TargetedMulti", rowPathsPartial, rowFormulasPartial, rowUpdatedAtsPartial);
                            NetworkCalling.SendCustomNetworkEvent(perPlayerHandler, VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner,
                                "RPC_SetCellStyle_TargetedMulti", rowPathsPartial, rowStylesPartial, rowStyleUpdatedAtsPartial);
                            index = 0;
                            sizeCounter = 5 * 4 + path.Length + formula.Length + 8 + 2*4; // reset size counter for next batch
                        }

                        rowPaths[index] = path;
                        rowFormulas[index] = formula;
                        rowUpdatedAts[index] = updatedAt;
                        rowStyles[index] = style;
                        rowStyleUpdatedAts[index] = styleUpdatedAt;
                        index++;
                    }

                    if (index == 0)
                        continue;
                    
                    if (index == cols)
                    {
                        NetworkCalling.SendCustomNetworkEvent(perPlayerHandler, VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner,
                            "RPC_SetCellFormula_TargetedMulti", rowPaths, rowFormulas, rowUpdatedAts);
                        NetworkCalling.SendCustomNetworkEvent(perPlayerHandler, VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner,
                            "RPC_SetCellStyle_TargetedMulti", rowPaths, rowStyles, rowStyleUpdatedAts);
                    }
                    else
                    {
                        var rowPathsPartial = new string[index];
                        var rowFormulasPartial = new string[index];
                        var rowUpdatedAtsPartial = new long[index];
                        var rowStylesPartial = new ulong[index];
                        var rowStyleUpdatedAtsPartial = new long[index];
                        Array.Copy(rowPaths, rowPathsPartial, index);
                        Array.Copy(rowFormulas, rowFormulasPartial, index);
                        Array.Copy(rowUpdatedAts, rowUpdatedAtsPartial, index);
                        Array.Copy(rowStyles, rowStylesPartial, index);
                        Array.Copy(rowStyleUpdatedAts, rowStyleUpdatedAtsPartial, index);
                        NetworkCalling.SendCustomNetworkEvent(perPlayerHandler, VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner,
                            "RPC_SetCellFormula_TargetedMulti", rowPathsPartial, rowFormulasPartial, rowUpdatedAtsPartial);
                        NetworkCalling.SendCustomNetworkEvent(perPlayerHandler, VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner,
                            "RPC_SetCellStyle_TargetedMulti", rowPathsPartial, rowStylesPartial, rowStyleUpdatedAtsPartial);
                    }
                }

                _log.Log($"Finished sending sync to player {forPlayerId}!");
                NetworkCalling.SendCustomNetworkEvent(this, VRC.Udon.Common.Interfaces.NetworkEventTarget.Others,
                    nameof(RPC_SetDone), forPlayerId);
            }
        }

        [NetworkCallable(maxEventsPerSecond: 1)]
        public void RPC_SetDone(int forPlayerId)
        {
            if (forPlayerId == Networking.LocalPlayer.playerId)
            {
                _log.Log($"Startup sync complete!");
                _loadingOverlay.SetActive(false);
                _syncDone = true;
            }
        }

        public void SetCellFormula(string path, string value)
        {
            NetworkCalling.SendCustomNetworkEvent(this, VRC.Udon.Common.Interfaces.NetworkEventTarget.All,
                nameof(RPC_SetCellFormula),
                path, value, Networking.GetNetworkDateTime().Ticks);
        }

        public void SetCellStyle(string path, ulong style)
        {
            NetworkCalling.SendCustomNetworkEvent(this, VRC.Udon.Common.Interfaces.NetworkEventTarget.All,
                nameof(RPC_SetCellStyle),
                path, style, Networking.GetNetworkDateTime().Ticks);
        }

        [NetworkCallable(maxEventsPerSecond: 100)]
        public void RPC_SetCellFormula(string path, string formula, long updatedAt)
        {
            var cellData = _cellHolder.CellsPath[path].DataDictionary;

            long lastUpdatedAt = cellData["updatedAt"].Long;
            if (updatedAt <= lastUpdatedAt)
            {
                if (updatedAt != long.MinValue) // no prior data at all doesn't deserve a log
                    _log.Log($"Ignoring outdated formula update for cell {path}. Current updatedAt: {lastUpdatedAt}, incoming updatedAt: {updatedAt}");
                return;
            }

            var reparse = cellData["formula"].IsEmpty || cellData["formula"].String != formula;
            cellData["formula"] = formula;
            cellData["updatedAt"] = updatedAt;

            _cellEvaluator.EvaluateCell(cellData, reparse); // enqueue

            TriggerCellValueCallbacks(path);
        }

        [NetworkCallable(maxEventsPerSecond: 100)]
        public void RPC_SetCellStyle(string path, ulong style, long updatedAt)
        {
            var cellData = _cellHolder.CellsPath[path].DataDictionary;

            long lastUpdatedAt = cellData["styleUpdatedAt"].Long;
            if (updatedAt <= lastUpdatedAt)
            {
                if (updatedAt != long.MinValue) // no prior data at all doesn't deserve a log
                    _log.Log($"Ignoring outdated style update for cell {path}. Current updatedAt: {lastUpdatedAt}, incoming updatedAt: {updatedAt}");
                return;
            }

            cellData["style"] = style;
            cellData["styleUpdatedAt"] = updatedAt;

            _styleHandler.RefreshStyle(cellData);
        }

        public void TriggerCellValueCallbacks(string path)
        {
            _selectionHandler.HandleCellValueChanged(path);
        }
    }
}