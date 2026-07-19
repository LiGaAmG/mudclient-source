// --------------------------------------------------------------------------------------------------------------------
// <copyright file="MessageConveyor.cs" company="Adamand MUD">
//   Copyright (c) Adamant MUD
// </copyright>
// <summary>
//   Defines the MessageConveyor type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using Adan.Client.Common.Settings;

namespace Adan.Client.Common.Conveyor
{
    #region Namespace Imports

    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using Model;
    using Utils;
    using Commands;
    using CommandSerializers;
    using ConveyorUnits;
    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;
    using MessageDeserializers;
    using Messages;
    using Networking;

    #endregion

    /// <summary>
    /// Conveyor that passess messages throught handlers.
    /// </summary>
    public sealed class MessageConveyor : IDisposable
    {

        #region Events

        /// <summary>
        /// Occurs when message if recieved from server.
        /// </summary>
        public event EventHandler<MessageReceivedEventArgs> MessageReceived;

        /// <summary>
        /// 
        /// </summary>
        public event EventHandler OnDisconnected;

        #endregion

        #region Constants and Fields

        private const string PluginToggleConfigFileName = "plugin-toggles.conf";

        private readonly IDictionary<int, IList<ConveyorUnit>> _conveyorUnitsByMessageType = new Dictionary<int, IList<ConveyorUnit>>();
        private readonly IDictionary<int, IList<ConveyorUnit>> _conveyorUnitsByCommandType = new Dictionary<int, IList<ConveyorUnit>>();
        private readonly IList<ConveyorUnit> _allConveyorUnits = new List<ConveyorUnit>();

        private readonly IList<CommandSerializer> _currentCommandSerializers = new List<CommandSerializer>();
        private readonly IList<MessageDeserializer> _currentMessageDeserializers = new List<MessageDeserializer>();
        private readonly MccpClient _mccpClient;
        private readonly byte[] _buffer = new byte[32767];

        private readonly ControlCodeAnalyser _analyzer = new ControlCodeAnalyser();

        private int _currentMessageType = BuiltInMessageTypes.TextMessage;

        // Метка последней неотвеченной отправки (Stopwatch ticks); 0 = ответ получен
        private long _rttSendTimestamp;
        private volatile bool _firstNetAfterConnect = false;

        #endregion

        #region Constructors and Destructors

        private MessageConveyor([NotNull] MccpClient mccpClient)
        {
            Assert.ArgumentNotNull(mccpClient, "mccpClient");

            _mccpClient = mccpClient;
            _mccpClient.DataReceived += HandleDataReceived;
            _mccpClient.NetworkError += HandleNetworkError;
            _mccpClient.Connected += HandleConnected;
            _mccpClient.Disconnected += HandleDisconnected;
        }

        public static MessageConveyor CreateNew(string name, string uid, ProfileHolder profile, IList<RootModel> allRootModels)
        {
            var result = new MessageConveyor(new MccpClient());
            var rootModel = new RootModel(result, profile, allRootModels)
            {
                Uid = uid,
            };

            allRootModels.Add(rootModel);
            result.RootModel = rootModel;
            return result;
        }

        public static MessageConveyor CreateNew(RootModel rootModel)
        {
            var result = new MessageConveyor(new MccpClient()) { RootModel = rootModel };
            return result;
        }

        #endregion

        #region Properties

        /// <summary>
        /// 
        /// </summary>
        public IDictionary<int, IList<ConveyorUnit>> ConveyorUnitsByCommandType
        {
            get
            {
                return _conveyorUnitsByCommandType;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public IDictionary<int, IList<ConveyorUnit>> ConveyorUnitsByMessageType
        {
            get
            {
                return _conveyorUnitsByMessageType;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public RootModel RootModel
        {
            get;
            private set;
        }

        /// <summary>
        /// Last connection host
        /// </summary>
        public string LastConnectHost
        {
            get;
            private set;
        }

        /// <summary>
        /// Last connection port
        /// </summary>
        public int LastConnectPort
        {
            get;
            private set;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Adds the command serializer.
        /// </summary>
        /// <param name="commandSerializer">The command serializer to add.</param>
        public void AddCommandSerializer([NotNull] CommandSerializer commandSerializer)
        {
            Assert.ArgumentNotNull(commandSerializer, "commandSerializer");

            _currentCommandSerializers.Add(commandSerializer);
        }

        /// <summary>
        /// Adds the message deserializer.
        /// </summary>
        /// <param name="messageDeserializer">The message deserializer to add.</param>
        public void AddMessageDeserializer([NotNull] MessageDeserializer messageDeserializer)
        {
            Assert.ArgumentNotNull(messageDeserializer, "messageDeserializer");

            _currentMessageDeserializers.Add(messageDeserializer);

        }

        /// <summary>
        /// Adds the conveyor unit.
        /// </summary>
        public void AddConveyorUnit([NotNull] ConveyorUnit conveyorUnit, bool addToTop = false)
        {
            Assert.ArgumentNotNull(conveyorUnit, "conveyorUnit");

            foreach (var handledMessageType in conveyorUnit.HandledMessageTypes)
            {
                if (!ConveyorUnitsByMessageType.ContainsKey(handledMessageType))
                {
                    ConveyorUnitsByMessageType[handledMessageType] = new List<ConveyorUnit>();
                }

                if (addToTop)
                {
                    ConveyorUnitsByMessageType[handledMessageType].Insert(0, conveyorUnit);
                }
                else
                {
                    ConveyorUnitsByMessageType[handledMessageType].Add(conveyorUnit);
                }
            }

            foreach (var handledCommandType in conveyorUnit.HandledCommandTypes)
            {
                if (!ConveyorUnitsByCommandType.ContainsKey(handledCommandType))
                {
                    ConveyorUnitsByCommandType[handledCommandType] = new List<ConveyorUnit>();
                }

                if (addToTop)
                {
                    ConveyorUnitsByCommandType[handledCommandType].Insert(0, conveyorUnit);
                }
                else
                {
                    ConveyorUnitsByCommandType[handledCommandType].Add(conveyorUnit);
                }
            }
            if (addToTop)
            {
                _allConveyorUnits.Insert(0, conveyorUnit);
            }
            else
            {
                _allConveyorUnits.Add(conveyorUnit);
            }
        }


        public void ImportJMC(string line, RootModel rootModel)
        {
            var command = new TextCommand(line);
            if (ConveyorUnitsByCommandType.ContainsKey(command.CommandType))
            {
                foreach (var conveyorUnit in ConveyorUnitsByCommandType[command.CommandType])
                {
                    try
                    {
                        conveyorUnit.HandleCommand(command, true);
                    }
                    catch { }

                    if (command.Handled)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Connects to the specified host.
        /// </summary>
        /// <param name="host">The host to connect to.</param>
        /// <param name="port">The port to connect to.</param>
        public void Connect([NotNull] string host, int port)
        {
            Assert.ArgumentNotNullOrWhiteSpace(host, "host");

            try
            {
                _mccpClient.Connect(host, port);

                LastConnectHost = host;
                LastConnectPort = port;
                PerfStats.ServerHost = host;
            }
            catch (Exception)
            {
                this.PushMessage(new ErrorMessage("Error connect to host {0}: {1}"));
            }
        }

        /// <summary>
        /// Disconnects this instance.
        /// </summary>
        public void Disconnect()
        {
            // Удаляем uid из RTT-индикатора — закрытый таб не должен торчать в списке
            System.Threading.Interlocked.Exchange(ref _rttSendTimestamp, 0);
            PerfStats.RemoveUid(RootModel != null ? RootModel.Uid : null);

            try
            {
                _mccpClient.Disconnect();
            }
            catch (Exception)
            { }

            if (OnDisconnected != null)
            {
                OnDisconnected(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Pushes the command.
        /// </summary>
        /// <param name="command">The text command to send.</param>
        public void PushCommand([NotNull] Command command)
        {
            Assert.ArgumentNotNull(command, "command");

            // Встроенная команда статистики производительности — перехватываем до юнитов,
            // чтобы парсер клиентских команд не ругался на неизвестную команду.
            var perfCommand = command as TextCommand;
            if (perfCommand != null)
            {
                var perfText = perfCommand.CommandText.Trim();
                if (perfText.Equals("#perf", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var line in PerfStats.BuildReport())
                        PushMessage(new InfoMessage(line));
                    command.Handled = true;
                    return;
                }
                if (perfText.Equals("#perf reset", StringComparison.OrdinalIgnoreCase))
                {
                    PerfStats.Reset();
                    PushMessage(new InfoMessage("Статистика производительности сброшена."));
                    command.Handled = true;
                    return;
                }
                if (perfText.Equals("#perf clear", StringComparison.OrdinalIgnoreCase))
                {
                    PerfLog.Clear();
                    PushMessage(new InfoMessage("Лог производительности очищен."));
                    command.Handled = true;
                    return;
                }
            }

            try
            {
                if (ConveyorUnitsByCommandType.ContainsKey(command.CommandType))
                {
                    foreach (var conveyorUnit in ConveyorUnitsByCommandType[command.CommandType])
                    {
                        conveyorUnit.HandleCommand(command);
                        if (command.Handled)
                        {
                            break;
                        }
                    }
                }

                if (!command.Handled)
                {
                    foreach (var commandSerializer in _currentCommandSerializers)
                    {
                        commandSerializer.SerializeAndSendCommand(command);
                        if (command.Handled)
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLogger.Instance.Write(string.Format("Error push command: {0}\r\n{1}", ex.Message, ex.StackTrace));
            }
        }

        /// <summary>
        /// Pushes the message into conveyor queue.
        /// </summary>
        /// <param name="message">The message to push.</param>
        public void PushMessage([NotNull] Message message)
        {
            Assert.ArgumentNotNull(message, "message");

            try
            {
                PerfStats.RecordMessage();
                if (ConveyorUnitsByMessageType.ContainsKey(message.MessageType))
                {
                    long totalMs = 0;
                    string msgTypeName = message.GetType().Name;
                    var slowUnits = new System.Text.StringBuilder();
                    foreach (var conveyorUnit in ConveyorUnitsByMessageType[message.MessageType])
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        conveyorUnit.HandleMessage(message);
                        sw.Stop();
                        PerfStats.RecordUnit(conveyorUnit.GetType().Name, sw.ElapsedTicks);
                        long elapsed = sw.ElapsedMilliseconds;
                        totalMs += elapsed;
                        if (elapsed >= 2)
                        {
                            PerfLog.Write(conveyorUnit.GetType().Name, msgTypeName, elapsed);
                            if (slowUnits.Length > 0) slowUnits.Append(", ");
                            slowUnits.Append(conveyorUnit.GetType().Name).Append(':').Append(elapsed).Append("ms");
                        }
                        if (message.Handled)
                        {
                            break;
                        }
                    }
                    if (totalMs >= 10)
                    {
                        var textMsg = message as Common.Messages.TextMessage;
                        var innerText = textMsg?.InnerText ?? "";
                        if (innerText.Length > 80) innerText = innerText.Substring(0, 80) + "...";
                        PerfLog.WriteTotal(msgTypeName, totalMs, slowUnits.ToString(), innerText);
                    }
                }

                if (message.Handled)
                {
                    var diagMsg = message as Common.Messages.TextMessage;
                    if (diagMsg != null)
                        ErrorLogger.Instance.Write(string.Format("[DIAG] TextMessage suppressed by conveyor: '{0}'", diagMsg.InnerText));
                    return;
                }

                if (MessageReceived != null)
                {
                    MessageReceived(this, new MessageReceivedEventArgs(message));
                }
            }
            catch (Exception ex)
            {
                ErrorLogger.Instance.Write(string.Format("Error push message: {{{0}}}\r\n{{{1}}}", ex.Message, ex.StackTrace));
            }
        }

        /// <summary>
        /// Sends the raw data to server.
        /// </summary>
        /// <param name="offset">The offset if <paramref name="data"/> array to start.</param>
        /// <param name="bytesToSend">The bytes to send.</param>
        /// <param name="data">The array of bytes to send.</param>
        public void SendRawDataToServer(int offset, int bytesToSend, [NotNull] byte[] data)
        {
            Assert.ArgumentNotNull(data, "data");

            try
            {
                _mccpClient.Send(data, offset, bytesToSend);

                // Расшифровываем отправляемую команду для лога (KOI8-R),
                // чтобы было видно, КТО спамит сервер.
                string sentText;
                try
                {
                    sentText = Encoding.GetEncoding(20866)
                        .GetString(data, offset, Math.Min(bytesToSend, 60))
                        .Replace("\r", "").Replace("\n", "\\n");
                }
                catch
                {
                    sentText = "?";
                }

                PerfLog.WriteSend(bytesToSend, RootModel != null ? RootModel.Uid : null, sentText);

                // Пассивный замер RTT: метка отправки; погасится первым же ответом сервера.
                // ICMP-пинг у части машин режется фаерволом, а этот замер идёт по живому
                // игровому соединению — это и есть настоящий "пинг игры".
                var nowStamp = System.Diagnostics.Stopwatch.GetTimestamp();
                if (System.Threading.Interlocked.CompareExchange(ref _rttSendTimestamp, nowStamp, 0) == 0)
                {
                    // Новая неотвеченная отправка — регистрируем для живого индикатора
                    PerfStats.SetPendingSend(RootModel != null ? RootModel.Uid : null, nowStamp);
                }
            }
            catch (Exception)
            {
                this.PushMessage(new ErrorMessage("Error send text to server"));
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            System.Threading.Interlocked.Exchange(ref _rttSendTimestamp, 0);
            PerfStats.RemoveUid(RootModel != null ? RootModel.Uid : null);

            if (_mccpClient != null)
            {
                _mccpClient.Dispose();
            }

            foreach (var conveyorUnit in _allConveyorUnits)
            {
                conveyorUnit.Dispose();
            }
        }

        #endregion

        #region Methods

        private void HandleDataReceived([NotNull] object sender, [NotNull] DataReceivedEventArgs e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(e, "e");
            
            try
            {
                if (_firstNetAfterConnect) { _firstNetAfterConnect = false; PerfLog.WriteTotal("FIRST_NET", 0, "first bytes from server after connect"); }
            var _netSw = System.Diagnostics.Stopwatch.StartNew();
                int offset = e.Offset;
                int actualBytesReceived = 0;
                int end =  e.Offset + e.BytesReceived;
                int bytesRecieved = e.BytesReceived;
                byte[] data = e.GetData();

                while (offset < end)
                {
                    if (_analyzer.ProcessNext(data[offset]))
                    {
                        ++offset;
                    }

                    if (_analyzer.State == ControlCode.NeedMore)
                    {
                        continue;
                    }

                    switch(_analyzer.State)
                    {
                        case ControlCode.NoCode:
                            _buffer[actualBytesReceived] = data[offset];
                            ++offset;
                            ++actualBytesReceived;
                            break;
                        case ControlCode.DoubleIAC:
                            _buffer[actualBytesReceived] = TelnetConstants.InterpretAsCommandCode;
                            ++actualBytesReceived;
                            break;
                        case ControlCode.GoAhead:
                            _buffer[actualBytesReceived] = 0xA;   // new line
                            ++actualBytesReceived;
                            break;
                        case ControlCode.EchoOn:
                            PushMessage(new ChangeEchoModeMessage(false));
                            break;
                        case ControlCode.EchoOff:
                            PushMessage(new ChangeEchoModeMessage(true));
                            break;
                        case ControlCode.CustomProtocol:
                            FlushBufferToDeserializer(actualBytesReceived, true);
                            actualBytesReceived = 0;
                            _currentMessageType = _analyzer.CustomProtocolCode;
                            break;
                        case ControlCode.SubNegOff:
                            FlushBufferToDeserializer(actualBytesReceived, true);
                            _currentMessageType = BuiltInMessageTypes.TextMessage;
                            actualBytesReceived = 0;
                            break;
                    }
                }

                if (actualBytesReceived > 0)
                    FlushBufferToDeserializer(actualBytesReceived, false);
                _netSw.Stop();
                if (e.BytesReceived > 0)
                {
                    PerfLog.WriteNet(e.BytesReceived, _netSw.ElapsedMilliseconds, RootModel != null ? RootModel.Uid : null);

                    // Закрываем замер RTT, если была неотвеченная отправка
                    var sendStamp = System.Threading.Interlocked.Exchange(ref _rttSendTimestamp, 0);
                    if (sendStamp != 0)
                    {
                        PerfStats.ClearPendingSend(RootModel != null ? RootModel.Uid : null);
                        long rttMs = (System.Diagnostics.Stopwatch.GetTimestamp() - sendStamp) * 1000 / System.Diagnostics.Stopwatch.Frequency;
                        PerfStats.RecordPing(rttMs, RootModel != null ? RootModel.Uid : null);
                        if (rttMs >= 1000)
                            PerfLog.WriteTotal("GAME_RTT", rttMs, RootModel != null ? RootModel.Uid : "?");

                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLogger.Instance.Write(string.Format("Error handle data received: {0}\r\n{1}", ex.Message, ex.StackTrace));
                PushMessage(new ErrorMessage(ex.Message));
            }
        }

        private void FlushBufferToDeserializer(int bytesCount, bool isComplete)
        {
            var deserializer = _currentMessageDeserializers.FirstOrDefault(d => d.DeserializedMessageType == _currentMessageType);
            if (deserializer == null)
            {
                // falling back to text if there is no
                //deserializer = _currentMessageDeserializers.First(d => d.DeserializedMessageType == BuiltInMessageTypes.TextMessage);
                return;
            }

            deserializer.DeserializeDataFromServer(0, bytesCount, _buffer, isComplete);
        }

        private void HandleConnected([NotNull] object sender, [NotNull] EventArgs e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(e, "e");

            PerfLog.WriteTotal("CONNECTED", 0, "tcp handshake done");
            _firstNetAfterConnect = true;
            PushMessage(new ConnectedMessage());
        }

        private void HandleDisconnected([NotNull] object sender, [NotNull] EventArgs e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(e, "e");

            PushMessage(new DisconnectedMessage(_mccpClient.TotalBytesReceived, _mccpClient.BytesDecompressed));
        }

        private void HandleNetworkError([NotNull] object sender, [NotNull] NetworkErrorEventArgs e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(e, "e");

            PushMessage(new NetworkErrorMessageEx(e.Exception));
        }

        private static bool IsConfigFlagEnabled(string flagName)
        {
            try
            {
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PluginToggleConfigFileName);
                if (!File.Exists(configPath))
                {
                    return true;
                }

                foreach (var rawLine in File.ReadAllLines(configPath))
                {
                    var line = rawLine == null ? string.Empty : rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                    {
                        continue;
                    }

                    var separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0 || separatorIndex == line.Length - 1)
                    {
                        continue;
                    }

                    var name = line.Substring(0, separatorIndex).Trim();
                    if (!name.Equals(flagName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var value = line.Substring(separatorIndex + 1).Trim();
                    bool isEnabled;
                    if (TryParseBoolean(value, out isEnabled))
                    {
                        return isEnabled;
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLogger.Instance.Write(string.Format("Failed to read config flag {0}: {1}\r\n{2}", flagName, ex.Message, ex.StackTrace));
            }

            return true;
        }

        private static bool TryParseBoolean(string value, out bool result)
        {
            if (bool.TryParse(value, out result))
            {
                return true;
            }

            switch (value.ToLowerInvariant())
            {
                case "1":
                case "on":
                case "yes":
                    result = true;
                    return true;
                case "0":
                case "off":
                case "no":
                    result = false;
                    return true;
                default:
                    result = true;
                    return false;
            }
        }

        #endregion
    }
}

