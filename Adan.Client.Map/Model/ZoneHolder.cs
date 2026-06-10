using System;
using Adan.Client.Common.Model;
using Adan.Client.Map.Messages;
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
                    var zoneName = _zoneManager.GetZoneName(mapMessage.ZoneId);
                    if (!string.IsNullOrEmpty(zoneName))
                        RootModel.CurrentZoneName = zoneName;

                    _zoneManager.ExecuteRoomAction(this);
                    _zoneManager.UpdateControl(this);

                    sw.Stop();
                    // Маячок пути шага: вся обработка смены комнаты (зона+травник+маршрут+карта)
                    Adan.Client.Common.Conveyor.PerfLog.WriteTotal("ROOMPROC", sw.ElapsedMilliseconds,
                        string.Format("room={0} zone={1} uid={2}", RoomId, ZoneId,
                            Uid != null && Uid.Length > 8 ? Uid.Substring(0, 8) : Uid));
                }
            }
        }
    }
}
