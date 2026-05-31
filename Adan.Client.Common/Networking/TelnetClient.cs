// --------------------------------------------------------------------------------------------------------------------
// <copyright file="TelnetClient.cs" company="Adamand MUD">
//   Copyright (c) Adamant MUD
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace Adan.Client.Common.Networking
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;

    /// <summary>
    /// Basic wrapper of <see cref="Socket"/> to perform asynchronous input/output.
    /// </summary>
    public class TelnetClient : IDisposable
    {
        #region Constants and Fields

        private readonly byte[] _buffer;
        private readonly AsyncCallback _receiveDataCallback;
        private readonly AsyncCallback _connectedCallback;
        private readonly AsyncCallback _dataSentCallback;
        private AsyncCallback _proxyConnectedCallback;
        private Socket _theSocket;
        private SocketError _error;
        private string _targetHost;
        private int _targetPort;

        #endregion

        #region Constructors and Destructors

        /// <summary>
        /// Initializes a new instance of the <see cref="TelnetClient"/> class.
        /// </summary>
        public TelnetClient()
        {
            _buffer = new byte[32767];
            _receiveDataCallback = new AsyncCallback(ReceiveData);
            _connectedCallback = new AsyncCallback(OnConnected);
            _dataSentCallback = new AsyncCallback(OnDataSent);
            _proxyConnectedCallback = new AsyncCallback(OnConnectedToProxy);
        }

        #endregion

        #region Events

        /// <summary>
        /// Occurs when data is received.
        /// </summary>
        public event EventHandler<DataReceivedEventArgs> DataReceived;

        /// <summary>
        /// Occurs when some network error happens.
        /// </summary>
        public event EventHandler<NetworkErrorEventArgs> NetworkError;

        /// <summary>
        /// Occurs when client is connected to server.
        /// </summary>
        public event EventHandler Connected;

        /// <summary>
        /// Occurs when connection to server is lost.
        /// </summary>
        public event EventHandler Disconnected;

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the SOCKS5 proxy hostname. If null or empty, proxy is not used.
        /// </summary>
        public string Socks5Host
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the SOCKS5 proxy port.
        /// </summary>
        public int Socks5Port
        {
            get;
            set;
        }

        /// <summary>
        /// Gets whether a SOCKS5 proxy is configured.
        /// </summary>
        public bool UseProxy
        {
            get { return !string.IsNullOrWhiteSpace(Socks5Host) && Socks5Port > 0; }
        }

        /// <summary>
        /// Gets the last error.
        /// </summary>
        /// <value>The last error.</value>
        public SocketError LastError
        {
            get { return _error; }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Tests a SOCKS5 proxy by connecting and performing a handshake to the specified target.
        /// Returns null on success, or an error message on failure.
        /// </summary>
        public static string TestSocks5Proxy(string proxyHost, int proxyPort, string testHost, int testPort, int timeoutMs = 5000)
        {
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    var result = socket.BeginConnect(proxyHost, proxyPort, null, null);
                    if (!result.AsyncWaitHandle.WaitOne(timeoutMs, false))
                    {
                        socket.Close();
                        return "Connection to proxy timed out";
                    }

                    socket.EndConnect(result);

                    socket.ReceiveTimeout = timeoutMs;
                    socket.SendTimeout = timeoutMs;

                    // SOCKS5 greeting: [ver=5, nmethods=1, method=0 (no auth)]
                    var greeting = new byte[] { 5, 1, 0 };
                    socket.Send(greeting);

                    var response = new byte[2];
                    if (socket.Receive(response) != 2)
                        return "No greeting response from proxy";

                    if (response[0] != 5)
                        return "Invalid SOCKS version: " + response[0];

                    if (response[1] != 0)
                        return "Proxy requires authentication (method=" + response[1] + ")";

                    // Build connect request with domain name
                    var domainBytes = Encoding.ASCII.GetBytes(testHost);
                    var request = new byte[7 + domainBytes.Length];
                    request[0] = 5; // ver
                    request[1] = 1; // cmd: connect
                    request[2] = 0; // rsv
                    request[3] = 3; // atyp: domain name
                    request[4] = (byte)domainBytes.Length;
                    Array.Copy(domainBytes, 0, request, 5, domainBytes.Length);
                    request[5 + domainBytes.Length] = (byte)((testPort >> 8) & 0xFF);
                    request[6 + domainBytes.Length] = (byte)(testPort & 0xFF);

                    socket.Send(request);

                    var connectResponse = new byte[10];
                    int bytesRead = socket.Receive(connectResponse);
                    if (bytesRead < 4)
                        return "No connect response from proxy";

                    if (connectResponse[0] != 5)
                        return "Invalid SOCKS version in connect response: " + connectResponse[0];

                    if (connectResponse[1] != 0)
                    {
                        var errorCodes = new Dictionary<byte, string>
                        {
                            {1, "General SOCKS server failure"},
                            {2, "Connection not allowed by ruleset"},
                            {3, "Network unreachable"},
                            {4, "Host unreachable"},
                            {5, "Connection refused by proxy"},
                            {6, "TTL expired"},
                            {7, "Command not supported"},
                            {8, "Address type not supported"},
                        };

                        string errorMsg;
                        if (!errorCodes.TryGetValue(connectResponse[1], out errorMsg))
                            errorMsg = "Unknown SOCKS error code " + connectResponse[1];

                        return errorMsg;
                    }

                    return null; // success
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public virtual void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Connects to the specified host and port.
        /// If <see cref="Socks5Host"/> is set, connects via SOCKS5 proxy.
        /// </summary>
        /// <param name="host">
        /// The host to connect to.
        /// </param>
        /// <param name="port">
        /// The port to use.
        /// </param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "It's ok here.")]
        public virtual void Connect([NotNull] string host, int port)
        {
            Validate.ArgumentNotNull(host, "host");

            _theSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            // Set and change keep alive
            _theSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            int size = 4;
            byte[] keepAliveArr = new byte[size * 3];
            Buffer.BlockCopy(BitConverter.GetBytes((uint)1), 0, keepAliveArr, 0, size);
            Buffer.BlockCopy(BitConverter.GetBytes((uint)1000), 0, keepAliveArr, size, size);
            Buffer.BlockCopy(BitConverter.GetBytes((uint)1000), 0, keepAliveArr, size* 2, size);

            _theSocket.IOControl(IOControlCode.KeepAliveValues, keepAliveArr, null);

            if (UseProxy)
            {
                _targetHost = host;
                _targetPort = port;
                _theSocket.BeginConnect(Socks5Host, Socks5Port, _proxyConnectedCallback, null);
            }
            else
            {
                _theSocket.BeginConnect(host, port, _connectedCallback, null);
            }
        }

        /// <summary>
        /// Disconnects this instance.
        /// </summary>
        public virtual void Disconnect()
        {
            if (_theSocket != null)
            {
                Dispose(true);
                OnDisconnected();
            }
        }

        /// <summary>
        /// Sends the specified buffer.
        /// </summary>
        /// <param name="buffer">The buffer containing data to send.</param>
        /// <param name="offset">The zero-based position in <paramref name="buffer"/> at which to begin sending data.</param>
        /// <param name="length">The number of bytes to send.</param>
        public virtual void Send([NotNull] byte[] buffer, int offset, int length)
        {
            Validate.ArgumentNotNull(buffer, "buffer");
            try
            {
                if (_theSocket != null)
                {
                    _theSocket.BeginSend(buffer, offset, length, SocketFlags.None, out _error, _dataSentCallback, null);
                }
            }
            catch (SocketException exception)
            {
                HandleSockedException(exception);
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// This method is called each time when some data is received from server.
        /// </summary>
        /// <param name="sender">
        /// The source of event.
        /// </param>
        /// <param name="e">
        /// Arguments of event.
        /// </param>
        protected virtual void OnDataReceived([NotNull] object sender, [NotNull] DataReceivedEventArgs e)
        {
            Validate.ArgumentNotNull(sender, "sender");
            Validate.ArgumentNotNull(e, "e");

            if (DataReceived != null)
            {
                DataReceived(sender, e);
            }
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                var socket = _theSocket;
                _theSocket = null;
                if (socket != null)
                {
                    socket.Dispose();
                }
            }
        }

        /// <summary>
        /// Called when this client looses connection.
        /// </summary>
        protected virtual void OnDisconnected()
        {
            if (Disconnected != null)
            {
                Disconnected(this, EventArgs.Empty);
            }
        }

        private void OnDataReceived(int count)
        {
            OnDataReceived(this, new DataReceivedEventArgs(count, 0, _buffer));
        }

        private void Initialize()
        {
            if (_theSocket != null)
            {
                _theSocket.BeginReceive(_buffer, 0, _buffer.Length, SocketFlags.None, out _error, _receiveDataCallback, null);
            }
        }

        private void ReceiveData([NotNull] IAsyncResult ar)
        {
            Validate.ArgumentNotNull(ar, "ar");
            try
            {
                if (_theSocket != null)
                {
                    var bytesRecieved = _theSocket.EndReceive(ar);
                    if (bytesRecieved > 0)
                    {
                        OnDataReceived(bytesRecieved);
                        Initialize();
                    }
                    else
                    {
                        OnDisconnected();
                    }
                }
            }
            catch (SocketException exception)
            {
                HandleSockedException(exception);
            }
        }

        private void OnConnectedToProxy([NotNull] IAsyncResult ar)
        {
            Assert.ArgumentNotNull(ar, "ar");
            try
            {
                if (_theSocket == null)
                    return;

                _theSocket.EndConnect(ar);

                // Perform SOCKS5 handshake synchronously
                PerformSocks5Handshake();

                Initialize();
                if (Connected != null)
                {
                    Connected(this, EventArgs.Empty);
                }
            }
            catch (SocketException exception)
            {
                HandleSockedException(exception);
            }
            catch (Exception exception)
            {
                HandleException(new NetworkErrorEventArgs(exception));
            }
        }

        private void PerformSocks5Handshake()
        {
            // SOCKS5 greeting: [ver=5, nmethods=1, method=0 (no auth)]
            byte[] greeting = { 5, 1, 0 };
            _theSocket.Send(greeting);

            byte[] response = new byte[2];
            int bytesRead = _theSocket.Receive(response);
            if (bytesRead != 2)
                throw new SocketException((int)SocketError.ConnectionRefused);

            if (response[0] != 5)
                throw new SocketException((int)SocketError.ProtocolNotSupported);

            if (response[1] != 0)
                throw new SocketException((int)SocketError.AccessDenied);

            // Build connect request with domain name
            byte[] domainBytes = Encoding.ASCII.GetBytes(_targetHost);
            byte[] request = new byte[7 + domainBytes.Length];
            request[0] = 5; // SOCKS version
            request[1] = 1; // CONNECT command
            request[2] = 0; // reserved
            request[3] = 3; // address type: domain name
            request[4] = (byte)domainBytes.Length;
            Array.Copy(domainBytes, 0, request, 5, domainBytes.Length);
            request[5 + domainBytes.Length] = (byte)((_targetPort >> 8) & 0xFF);
            request[6 + domainBytes.Length] = (byte)(_targetPort & 0xFF);

            _theSocket.Send(request);

            byte[] connectResponse = new byte[10];
            bytesRead = _theSocket.Receive(connectResponse);
            if (bytesRead < 4)
                throw new SocketException((int)SocketError.ConnectionRefused);

            if (connectResponse[0] != 5)
                throw new SocketException((int)SocketError.ProtocolNotSupported);

            if (connectResponse[1] != 0)
            {
                throw new SocketException((int)SocketError.ConnectionRefused);
            }
        }

        private void OnConnected([NotNull] IAsyncResult ar)
        {
            Assert.ArgumentNotNull(ar, "ar");
            try
            {
                if (_theSocket != null)
                {
                    _theSocket.EndConnect(ar);

                    Initialize();
                    if (Connected != null)
                    {
                        Connected(this, EventArgs.Empty);
                    }
                }
            }
            catch (SocketException exception)
            {
                HandleSockedException(exception);
            }
        }

        private void OnDataSent([NotNull] IAsyncResult ar)
        {
            Assert.ArgumentNotNull(ar, "ar");

            try
            {
                if (_theSocket != null)
                {
                    _theSocket.EndSend(ar);
                }
            }
            catch (SocketException exception)
            {
                HandleSockedException(exception);
            }
        }

        private void HandleSockedException([NotNull] SocketException exception)
        {
            Assert.ArgumentNotNull(exception, "exception");
            Dispose(true);
            HandleException(new NetworkErrorEventArgs(exception));
        }

        /// <summary>
        /// Handles a network error by raising <see cref="NetworkError"/> event.
        /// </summary>
        /// <param name="exception">The exception details.</param>
        protected void HandleException(NetworkErrorEventArgs exception)
        {
            if (NetworkError != null)
            {
                NetworkError(this, exception);
            }
        }

        #endregion
    }
}
