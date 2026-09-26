using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;

namespace pi.vrcalc
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PerPlayerHolder : UdonSharpBehaviour
    {
        [SerializeField] private DebugLogger _log;

        private DataDictionary _playerObjects;

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player))
                return;

            if (_playerObjects == null)
                _playerObjects = new DataDictionary();

            var playerId = new DataToken(player.playerId);
            if (!_playerObjects.ContainsKey(playerId))
            {
                var playerObjs = Networking.GetPlayerObjects(player);
                foreach (var obj in playerObjs)
                {
                    var handler = obj.GetComponent<PerPlayerHandler>();
                    if (handler != null)
                    {
                        _playerObjects[playerId] = handler;
                        _log.Log($"Registered PerPlayerHandler for player {player.displayName} ({player.playerId})");
                        break;
                    }
                }
            }
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player))
                return;

            _playerObjects.Remove(new DataToken(player.playerId));
        }

        public PerPlayerHandler GetHandlerForPlayer(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player))
                return null;

            return GetHandlerForPlayer(player.playerId);
        }

        public PerPlayerHandler GetHandlerForPlayer(int playerId)
        {
            var playerToken = new DataToken(playerId);
            if (_playerObjects != null && _playerObjects.TryGetValue(playerToken, out var handler) && handler.Reference != null)
                return (PerPlayerHandler)handler.Reference;

            return null;
        }

        public PerPlayerHandler GetHandlerForLocalPlayer()
        {
            var localPlayer = Networking.LocalPlayer;
            return GetHandlerForPlayer(localPlayer);
        }
    }
}