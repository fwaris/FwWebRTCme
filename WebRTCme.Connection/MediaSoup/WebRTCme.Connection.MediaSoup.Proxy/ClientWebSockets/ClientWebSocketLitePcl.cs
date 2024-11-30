using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Reactive;
using System.Reactive.Linq;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using WebsocketClientLite.PCL;

namespace WebRTCme.Connection.MediaSoup.ClientWebSockets
{
    class ClientWebSocketLitePcl : IClientWebSocket
    {
        private class ClientWebSocketOptionsLitePcl : IClientWebSocketOptions
        {
            readonly MessageWebsocketRx _baseWebSocket;

            public ClientWebSocketOptionsLitePcl(MessageWebsocketRx baseWebSocket)
            {
                _baseWebSocket = baseWebSocket;
            }

            public bool IgnoreServerCertificateErrors
            {
                get => _baseWebSocket.IgnoreServerCertificateErrors;
                set => _baseWebSocket.IgnoreServerCertificateErrors = value;
            }

            public void AddSubProtocol(string subProtocol)
            {
                _baseWebSocket.Subprotocols = _baseWebSocket.Subprotocols.Concat(new string[] { subProtocol });
            }

            public void SetRequestHeader(string headerName, string headerValue)
            {
                _baseWebSocket.Headers.Add(headerName, headerValue);
            }
        }

        readonly MessageWebsocketRx _baseWebSocket;
        readonly IClientWebSocketOptions _options;
        Channel<string> _channel;
        IDisposable _receiverDisposable;

        public ClientWebSocketLitePcl()
        {
            _baseWebSocket = new MessageWebsocketRx
            {
                Headers = new Dictionary<string, string> { { "Pragma", "no-cache" }, { "Cache-Control", "no-cache" } },
                TlsProtocolType = SslProtocols.Tls12,
                Subprotocols = new string[] { "protoo", "Sec-WebSocket-Protocol" }
            };
            _options = new ClientWebSocketOptionsLitePcl(_baseWebSocket);
        }

        public IClientWebSocketOptions Options => _options;

        public Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription,
            CancellationToken cancellationToken)
        {
            _receiverDisposable.Dispose();
            _channel.Writer.Complete();
            //
            _baseWebSocket.Dispose();
            return Task.CompletedTask;
            //return _baseWebSocket.DisconnectAsync();
        }

        private void HandleEvent(IWebsocketClientLite.PCL.IDataframe? dataframe, IWebsocketClientLite.PCL.ConnectionStatus status, TaskCompletionSource<Unit> tcs)
        {
            if (status == IWebsocketClientLite.PCL.ConnectionStatus.WebsocketConnected)
            {
                Console.WriteLine($"======> Connect: {status}");
                tcs.TrySetResult(Unit.Default);
            }
            else if (status == IWebsocketClientLite.PCL.ConnectionStatus.Disconnected ||
                     status == IWebsocketClientLite.PCL.ConnectionStatus.Aborted ||
                     status == IWebsocketClientLite.PCL.ConnectionStatus.ConnectionFailed)
            {
                tcs.TrySetException(new WebSocketException("Connection failed"));
            }
            else if (status == IWebsocketClientLite.PCL.ConnectionStatus.DataframeReceived)
            {
                var message = dataframe?.Message;
                var ok = _channel.Writer.TryWrite(message);
                Debug.Assert(ok);
                if (!ok)
                {
                    // TODO: Error logging.
                    Console.WriteLine($"ERROR: Channel is full");
                }
            }
        }

        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            _channel = Channel.CreateBounded<string>(5); 
            TaskCompletionSource<Unit> tcs = new();
            using (cancellationToken.Register(() => { tcs.TrySetCanceled(); }))
            {
                var wsConnectionObservable = _baseWebSocket.WebsocketConnectWithStatusObservable(uri,hasClientPing:true);
                _receiverDisposable = wsConnectionObservable.Subscribe(
                    tuple => HandleEvent(tuple.Item1, tuple.Item2, tcs),
                    ex =>
                    {
                        // TODO: Error logging.
                    },
                    () =>
                    {
                    });
            }
            return Task.CompletedTask;
        }
/*
    public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            _channel = Channel.CreateBounded<string>(5); 
            TaskCompletionSource<Unit> tcs = new();

            using (cancellationToken.Register(() =>
            {
                tcs.TrySetCanceled();
            }))
            {
                var connectionDisposable = _baseWebSocket.ConnectionStatusObservable.Subscribe(
                    status =>
                    {
                        Console.WriteLine($"======> Connect: {status}");
                        if (status == IWebsocketClientLite.PCL.ConnectionStatus.WebsocketConnected)
                        {
                            tcs.TrySetResult(Unit.Default);
                        }
                        else if (status == IWebsocketClientLite.PCL.ConnectionStatus.Disconnected ||
                            status == IWebsocketClientLite.PCL.ConnectionStatus.Aborted ||
                            status == IWebsocketClientLite.PCL.ConnectionStatus.ConnectionFailed)
                        {
                            tcs.TrySetException(new WebSocketException("Connection failed"));
                        }
                    },
                    ex =>
                    {
                        tcs.TrySetException(ex);
                    },
                    () =>
                    {
                        tcs.TrySetResult(Unit.Default);
                    });

                _receiverDisposable = _baseWebSocket.MessageReceiverObservable.Subscribe(
                    message =>
                    {
                        var ok = _channel.Writer.TryWrite(message);
                        Debug.Assert(ok);
                        if (!ok)
                        {
                            // TODO: Error logging.
                            Console.WriteLine($"ERROR: Channel is full");
                        }
                    },
                    ex =>
                    {
                        // TODO: Error logging.
                    },
                    () =>
                    {
                    });


                await _baseWebSocket.ConnectAsync(uri);
                try
                {
                    _ = await tcs.Task;
                }
                catch (WebSocketException)
                {
                    _receiverDisposable.Dispose();
                    connectionDisposable.Dispose();
                    throw;
                }
                catch 
                {
                    _receiverDisposable.Dispose();
                    throw;
                }
                connectionDisposable.Dispose();
            }
        }
*/
        public async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, 
            CancellationToken cancellationToken)
        {
            var message = await _channel.Reader.ReadAsync(cancellationToken);
            var bytes = Encoding.UTF8.GetBytes(message);
            bytes.CopyTo(buffer.Array, 0);
            return new WebSocketReceiveResult(bytes.Length, WebSocketMessageType.Text, true);
        }

        public Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, 
            CancellationToken cancellationToken)
        {
            return _baseWebSocket.GetSender().SendText(Encoding.UTF8.GetString(buffer.ToArray()),cancellationToken);
        }
    }
}
