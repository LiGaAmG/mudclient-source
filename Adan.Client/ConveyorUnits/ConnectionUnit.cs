// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ConnectionUnit.cs" company="Adamand MUD">
//   Copyright (c) Adamant MUD
// </copyright>
// <summary>
//   Defines the ConnectionUnit type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------


namespace Adan.Client.ConveyorUnits
{
    using System.Collections.Generic;
    using System.Globalization;
    using Common.Settings;
    using Common.Conveyor;
    using Commands;
    using Common.Commands;
    using Common.ConveyorUnits;
    using Common.Messages;
    using CSLib.Net.Diagnostics;
    using Properties;

    /// <summary>
    /// Conveyor unit responsible for connection handling.
    /// </summary>
    public class ConnectionUnit : ConveyorUnit
    {
        private static long _nextConnectTick = 0;
        private static readonly object _staggerLock = new object();

        public ConnectionUnit(MessageConveyor conveyor) : base(conveyor)
        {
        }
 
        #region Overrides of ConveyorUnit

        /// <summary>
        /// Gets a set of message types that this unit can handle.
        /// </summary>
        public override IEnumerable<int> HandledMessageTypes
        {
            get
            {
                return new[] { BuiltInCommandTypes.TextCommand, BuiltInCommandTypes.ConnectionCommands };
            }
        }

        /// <summary>
        /// Gets a set of command types that this unit can handle.
        /// </summary>
        public override IEnumerable<int> HandledCommandTypes
        {
            get
            {
                return new[] { BuiltInCommandTypes.ConnectionCommands, BuiltInCommandTypes.TextCommand };
            }
        }

        public override void HandleCommand(Command command, bool isImport = false)
        {
            Assert.ArgumentNotNull(command, "command");

            var connectCommand = command as ConnectCommand;
            if (connectCommand != null)
            {
                connectCommand.Handled = true;
                if (Conveyor.RootModel.Connected || Conveyor.RootModel.ConnectionInProgress)
                {
                    PushMessageToConveyor(new ErrorMessage(Resources.AlreadyConnected));
                }
                else
                {
                    PushMessageToConveyor(new InfoMessage(string.Format(CultureInfo.CurrentUICulture, Resources.TryingToConnect, connectCommand.Host, connectCommand.Port)));
                    Conveyor.RootModel.ConnectionInProgress = true;
                    var _host = connectCommand.Host;
                    var _port = connectCommand.Port;
                    var _conv = Conveyor;
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        long delayMs = 0;
                        lock (_staggerLock)
                        {
                            long now = System.Diagnostics.Stopwatch.GetTimestamp();
                            long freq = System.Diagnostics.Stopwatch.Frequency;
                            long until = System.Threading.Interlocked.Read(ref _nextConnectTick);
                            if (until > now) delayMs = (until - now) * 1000 / freq;
                            long slot = System.Math.Max(now, until) + freq + freq / 2;
                            System.Threading.Interlocked.Exchange(ref _nextConnectTick, slot);
                        }
                        if (delayMs > 0) System.Threading.Thread.Sleep((int)System.Math.Min((long)delayMs, 30000L));
                        _conv.Connect(_host, _port);
                    });
                }

                return;
            }

            var disconnectCommand = command as DisconnectCommand;
            if (disconnectCommand != null)
            {
                disconnectCommand.Handled = true;
                if (!(Conveyor.RootModel.Connected || Conveyor.RootModel.ConnectionInProgress))
                {
                    PushMessageToConveyor(new ErrorMessage(Resources.NotConnected));
                }
                else
                {
                    Conveyor.Disconnect();
                }

                return;
            }

            if (!Conveyor.RootModel.Connected)
            {
                command.Handled = true;
                PushMessageToConveyor(new ErrorMessage(Resources.NotConnectedPleaseConnectFirst));
            }
        }

        public override void HandleMessage(Message message)
        {
            Assert.ArgumentNotNull(message, "message");

            var connectedMessage = message as ConnectedMessage;
            if (connectedMessage != null)
            {
                PushMessageToConveyor(new InfoMessage(Resources.ConnectionEstablished));
                Conveyor.RootModel.Connected = true;
                Conveyor.RootModel.ConnectionInProgress = false;
                return;
            }

            var disconnectedMessage = message as DisconnectedMessage;
            if (disconnectedMessage != null)
            {
                if (Conveyor.RootModel.Connected)
                {
                    Conveyor.RootModel.Connected = false;
                    Conveyor.RootModel.ConnectionInProgress = false;
                    PushMessageToConveyor(new InfoMessage(Resources.ConnectionLost));
                    PushMessageToConveyor(
                        new InfoMessage(
                            string.Format(
                                CultureInfo.InvariantCulture,
                                Resources.ConnectionStatistic,
                                disconnectedMessage.TotalBytesReceived,
                                disconnectedMessage.BytesDecompressed,
                                100.0f * (disconnectedMessage.TotalBytesReceived / (float)disconnectedMessage.BytesDecompressed))));
                }

                return;
            }

            var networkErrorMessage = message as NetworkErrorMessageEx;
            if (networkErrorMessage != null)
            {
                Conveyor.RootModel.Connected = false;
                Conveyor.RootModel.ConnectionInProgress = false;
                PushMessageToConveyor(new ErrorMessage(string.Format("#{0}", networkErrorMessage.Exception.Message)));
                PushMessageToConveyor(new ErrorMessage("#Disconnect"));

                //Автореконнект
                if (SettingsHolder.Instance.Settings.AutoReconnect)
                {
                    Conveyor.RootModel.PushCommandToConveyor(new ConnectCommand(Conveyor.LastConnectHost, Conveyor.LastConnectPort));
                }
            }
        }

        #endregion
    }
}

