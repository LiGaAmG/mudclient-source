using System;
using System.Linq;
using Adan.Client.Common.Model;
using Adan.Client.Common.Scripting;
using Adan.Client.Map.Messages;
using Adan.Client.Map.ViewModel;
using CSLib.Net.Annotations;

namespace Adan.Client.Map.Model
{
    /// <summary>
    /// 
    /// </summary>
    public class ZoneHolder
    {
        private readonly ZoneManager _zoneManager;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="zoneManager"></param>
        /// <param name="rootModel"></param>
        public ZoneHolder(ZoneManager zoneManager, [NotNull] RootModel rootModel)
        {
            _zoneManager = zoneManager;
            RootModel = rootModel;
            Uid = rootModel.Uid;
            ZoneId = -1;
            RoomId = -1;

            rootModel.MessageConveyor.MessageReceived += MessageConveyor_MessageReceived;
            rootModel.MessageConveyor.OnDisconnected += MessageConveyor_OnDisconnected;
        }

        private void MessageConveyor_OnDisconnected(object sender, EventArgs e)
        {
            ZoneId = -1;
            RoomId = -1;
            _zoneManager.UpdateControl(this);
        }

        /// <summary>
        /// 
        /// </summary>
        public string Uid
        {
            get;
            private set;
        }

        /// <summary>
        /// 
        /// </summary>
        public int RoomId
        {
            get;
            private set;
        }

        /// <summary>
        /// 
        /// </summary>
        public int ZoneId
        {
            get;
            private set;
        }

        /// <summary>
        /// 
        /// </summary>
        public RootModel RootModel { get; }

        private void MessageConveyor_MessageReceived(object sender, Common.Conveyor.MessageReceivedEventArgs e)
        {
            if (e.Message.MessageType == Constants.CurrentRoomMessageType)
            {
                var mapMessage = e.Message as CurrentRoomMessage;
                if (RoomId != mapMessage.RoomId || ZoneId != mapMessage.ZoneId)
                {
                    // Комната доехала — гасим живой замер ожидания шага
                    Adan.Client.Common.Conveyor.PerfStats.RoomWaitEnded();

                    var sw = System.Diagnostics.Stopwatch.StartNew();

                    RoomId = mapMessage.RoomId;
                    ZoneId = mapMessage.ZoneId;

                    // Update RootModel so other plugins (e.g. lore) can read current zone
                    var zoneViewModel = _zoneManager.GetZone(ZoneId);
                    var zoneName = zoneViewModel?.Name ?? string.Empty;
                    if (!string.IsNullOrEmpty(zoneName))
                        RootModel.CurrentZoneName = zoneName;

                    _zoneManager.ExecuteRoomAction(this);
                    _zoneManager.UpdateControl(this);
                    // Route background processing: drives routes on tabs not currently displayed.
                    if (zoneViewModel != null)
                        _zoneManager.RouteManager?.ProcessRoomChangeForTab(RootModel, zoneViewModel.AllRooms.FirstOrDefault(r => r.RoomId == RoomId), zoneViewModel);

                    // Lua room-change AFTER the route command so routing never waits
                    // for Lua table construction or coroutine resumption.
                    RootModel.ScriptHost.RaiseRoomChanged(RoomId, ZoneId, BuildRoomInfo(zoneViewModel, zoneName));

                    sw.Stop();
                    // Маячок пути шага: вся обработка смены комнаты (зона+травник+маршрут+карта)
                    Adan.Client.Common.Conveyor.PerfLog.WriteTotal("ROOMPROC", sw.ElapsedMilliseconds,
                        string.Format("room={0} zone={1} uid={2}", RoomId, ZoneId,
                            Uid != null && Uid.Length > 8 ? Uid.Substring(0, 8) : Uid));
                }
            }
        }

        /// <summary>
        /// Builds the local-map snapshot passed to Lua's on_room_change
        /// (see <see cref="Scripting.LuaScriptHost.RaiseRoomChanged"/>).
        /// Returns null if the room isn't in the locally loaded zone data
        /// (e.g. an unmapped/uncharted room) -- that's a normal, expected
        /// case, not an error.
        /// </summary>
        private RoomInfo BuildRoomInfo(ZoneViewModel zoneViewModel, string zoneName)
        {
            var roomViewModel = zoneViewModel?.AllRooms.FirstOrDefault(r => r.RoomId == RoomId);
            if (roomViewModel == null)
            {
                return null;
            }

            var room = roomViewModel.Room;
            var info = new RoomInfo
            {
                // AdditionalRoomParameters only defaults RoomAlias to ""
                // in its constructor -- Comments has no such default and
                // is plain null for any room that never had a comment set
                // (confirmed in AdditionalRoomParameters.cs). A null here
                // would reach Lua as nil instead of an empty string, and
                // ".." (Lua string concat) throws on nil -- coerce every
                // string field to "" so the Help-documented
                // `if __last_room.Comments ~= "" then` pattern is actually
                // safe to use, never crashes on an unset field.
                ZoneName = zoneName ?? string.Empty,
                Name = room.Name ?? string.Empty,
                Description = room.Description ?? string.Empty,
                X = room.XLocation,
                Y = room.YLocation,
                Z = room.ZLocation,
                Alias = roomViewModel.AdditionalRoomParameters.RoomAlias ?? string.Empty,
                Comments = roomViewModel.AdditionalRoomParameters.Comments ?? string.Empty,
                HasBeenVisited = roomViewModel.AdditionalRoomParameters.HasBeenVisited,
                HasHerb = roomViewModel.AdditionalRoomParameters.HasHerb,
                HerbDangerLevel = roomViewModel.AdditionalRoomParameters.HerbDangerLevel.ToString(),
            };

            foreach (var exit in room.Exits)
            {
                info.Exits.Add(new RoomExitInfo { Direction = exit.Direction.ToString(), RoomId = exit.RoomId });
            }

            return info;
        }
    }
}
