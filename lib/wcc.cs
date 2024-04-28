using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace WebsocketCollab
{
    /// <summary>
    /// Protocol-compliant message format.
    /// </summary>
    struct ProtocolMessageUnknown
    {
        /// <summary>
        /// Message as json string, this is not part of the message.
        /// </summary>
        public string jsonString;

        /// <summary>
        /// Protocol version of the message.
        /// </summary>
        public int version;

        /// <summary>
        /// Type of message, usually "message" or "data", but can also be any other non protocol-compliant string.
        /// </summary>
        public string type;

        /// <summary>
        /// Username of the message sender, this is not the name of the AI vtuber.
        /// </summary>
        public string from;

        /// <summary>
        /// Array of usernames to send the message to. If contains the string "all", then every participants should receive the message.
        /// </summary>
        public string[] to;

        /// <summary>
        /// JSON payload in the form of a Dictionary. Further parsing is required to safely use.
        /// </summary>
        public Dictionary<string, object> payload;

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>()
            {
                { "version", version },
                { "type", type },
                { "from", from },
                { "to", to },
                { "payload", payload },
            };
        }

        public override string ToString()
        {
            return jsonString;
        }
    }

    struct Payload
    {
        /// <summary>
        /// Name of the AI Vtuber that sent the message, or label of the data depending on message type.
        /// </summary>
        public string name;

        /// <summary>
        /// Content of the message, or data depending on message type.
        /// </summary>
        public string content;

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>()
            {
                { "name", name },
                { "content", content },
            };
        }
    }

    struct ProtocolMessage
    {
        /// <summary>
        /// Message as json string, this is not part of the message.
        /// </summary>
        public string jsonString;

        /// <summary>
        /// Protocol version of the message.
        /// </summary>
        public int version;

        /// <summary>
        /// Type of message, usually "message" or "data", but can also be any other non protocol-compliant string.
        /// </summary>
        public string type;

        /// <summary>
        /// Username of the message sender, this is not the name of the AI vtuber.
        /// </summary>
        public string from;

        /// <summary>
        /// Array of usernames to send the message to. If contains the string "all", then every participants should receive the message.
        /// </summary>
        public string[] to;

        /// <summary>
        /// Content of the message, with a name and content field.
        /// </summary>
        public Payload payload;

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>()
            {
                { "version", version },
                { "type", type },
                { "from", from },
                { "to", to },
                { "payload", payload.ToDictionary() },
            };
        }

        public override string ToString()
        {
            return jsonString;
        }
    }

    /// <summary>
    /// Collab client instance.
    /// </summary>
    class WebsocketCollabClient
    {
        /// <summary>
        /// Version of the protocol.
        /// </summary>
        public readonly int ProtocolVersion = 1;

        /// <summary>
        /// If the client is currently connected to a websocket server.
        /// </summary>
        public bool Connected = false;

        /// <summary>
        /// ID of the channel the client is currently connected to.
        /// </summary>
        public string ChannelId = string.Empty;

        /// <summary>
        /// Uri of the server the client is currently connected to.
        /// </summary>
        public Uri? ServerUrl = null;

        private string User = string.Empty;
        private ClientWebSocket? Socket = null;
        private Thread? ListenerThread = null;
        private bool ShouldCloseListenerThread = false;

        private List<Func<ProtocolMessageUnknown, Task>> ListenersAll = [];
        private List<Func<ProtocolMessage, Task>> ListenersText = [];
        private List<Func<ProtocolMessage, Task>> ListenersData = [];
        private List<Func<ProtocolMessageUnknown, Task>> ListenersOther = [];
        private List<Func<string, Task>> ListenersNonProtocol = [];

        private async Task ConnectWebsocket(Uri uri, string username, string password)
        {
            Socket = new ClientWebSocket();
            string credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
            Socket.Options.SetRequestHeader("Authorization", $"Basic {credentials}");
            await Socket.ConnectAsync(uri, CancellationToken.None);
        }

        private async Task SendString(ClientWebSocket socket, string payload)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            ArraySegment<byte> segment = new ArraySegment<byte>(bytes, 0, bytes.Length);
            await socket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private async Task<string> ReceiveString(ClientWebSocket socket)
        {
            WebSocketReceiveResult result;
            string receivedMessage = "";
            var message = new ArraySegment<byte>(new byte[4096]);
            do
            {
                result = await socket.ReceiveAsync(message, CancellationToken.None);
                if (result.MessageType != WebSocketMessageType.Text)
                    break;
                var messageBytes = message.Skip(message.Offset).Take(result.Count).ToArray();
                receivedMessage += Encoding.UTF8.GetString(messageBytes);
            }
            while (!result.EndOfMessage);
            return receivedMessage;
        }

        private Uri CreateUri(string url, string channel_id, string user, string password)
        {
            Uri uri = new Uri(url);
            string reconstructed_url = "";

            try
            {
                string scheme = uri.Scheme;
                reconstructed_url += scheme + "://";
            }
            catch { }

            reconstructed_url += $"{user}:{password}@";

            try
            {
                string host = uri.Host;
                reconstructed_url += host;
            }
            catch { }

            try
            {
                int port = uri.Port;
                if (port != 80)
                {
                    reconstructed_url += ":" + port.ToString();
                }
            }
            catch { }

            reconstructed_url += $"/{channel_id}";

            return new Uri(reconstructed_url);
        }

        private async Task CallListeners<T>(List<Func<T, Task>> listeners, T arg)
        {
            foreach (var listener in listeners)
                await listener(arg);
        }

        private async Task Listener()
        {
            while (!ShouldCloseListenerThread)
            {
                string received;
                try
                {
                    received = await ReceiveString(Socket!);
                }
                catch
                {
                    continue;
                }

                using (JsonDocument json = JsonDocument.Parse(received))
                {
                    JsonElement root = json.RootElement;

                    JsonElement versionElement = root.GetProperty("version");
                    JsonElement typeElement = root.GetProperty("type");
                    JsonElement fromElement = root.GetProperty("from");
                    JsonElement toElement = root.GetProperty("to");
                    JsonElement payloadElement = root.GetProperty("payload");

                    if (
                        versionElement.ValueKind != JsonValueKind.Number ||
                        typeElement.ValueKind != JsonValueKind.String ||
                        fromElement.ValueKind != JsonValueKind.String ||
                        toElement.ValueKind != JsonValueKind.Array ||
                        payloadElement.ValueKind != JsonValueKind.Object
                    )
                    {
                        await CallListeners(ListenersNonProtocol, received);
                        continue;
                    }

                    int versionInt = JsonSerializer.Deserialize<int>(versionElement.GetRawText());
                    string? typeString = JsonSerializer.Deserialize<string>(typeElement.GetRawText());
                    string? fromString = JsonSerializer.Deserialize<string>(fromElement.GetRawText());
                    string[]? toArr = JsonSerializer.Deserialize<string[]>(toElement.GetRawText());
                    Dictionary<string, object>? payloadDict = JsonSerializer.Deserialize<Dictionary<string, object>>(payloadElement.GetRawText());

                    if (typeString == null || fromString == null || toArr == null || payloadDict == null)
                    {
                        await CallListeners(ListenersNonProtocol, received);
                        continue;
                    }

                    ProtocolMessageUnknown protoMessageUnknown = new ProtocolMessageUnknown()
                    {
                        jsonString = received,
                        version = versionInt,
                        type = typeString,
                        from = fromString,
                        to = toArr,
                        payload = payloadDict,
                    };

                    await CallListeners(ListenersAll, protoMessageUnknown);

                    if (!(toArr.Contains("all") || toArr.Contains(User)))
                    {
                        continue;
                    };

                    if (fromString == User)
                    {
                        continue;
                    };

                    if (typeString == "message" || typeString == "data")
                    {
                        Payload payload;
                        try
                        {
                            object? nameString = payloadDict.GetValueOrDefault("name");
                            object? contentString = payloadDict.GetValueOrDefault("name");
                            if (nameString == null || contentString == null) throw new Exception();
                            if (nameString.GetType() != typeof(string) || contentString.GetType() != typeof(string)) throw new Exception();

                            payload = new Payload()
                            {
                                name = (string)nameString,
                                content = (string)contentString
                            };
                        }
                        catch
                        {
                            await CallListeners(ListenersOther, protoMessageUnknown);
                            continue;
                        }

                        ProtocolMessage protoMessage = new ProtocolMessage()
                        {
                            jsonString = received,
                            version = versionInt,
                            type = typeString,
                            from = fromString,
                            to = toArr,
                            payload = payload,
                        };

                        if (typeString == "message")
                        {
                            await CallListeners(ListenersText, protoMessage);
                            continue;
                        }
                        else if (typeString == "data")
                        {
                            await CallListeners(ListenersData, protoMessage);
                            continue;
                        }
                    }
                    else
                    {
                        await CallListeners(ListenersOther, protoMessageUnknown);
                        continue;
                    }
                }
            }
        }

        private void ListenerThreadLoader()
        {
            Task task = Task.Run(Listener);
            task.Wait();
        }

        private void StartListenerThread()
        {
            ListenerThread = new Thread(new ThreadStart(ListenerThreadLoader));
            ListenerThread.Priority = ThreadPriority.BelowNormal;
            ListenerThread.IsBackground = true;
            ListenerThread.Start();
        }

        private void ClearServerData()
        {
            User = "";
            ChannelId = "";
            ServerUrl = null;
            Connected = false;
        }

        /// <summary>
        /// Connects to the WebSocket server and joins the selected channel.
        /// </summary>
        /// <param name="url">Webscoket server URL</param>
        /// <param name="channel_id">ID of the channel to join</param>
        /// <param name="user">Username</param>
        /// <param name="password">Password</param>
        /// <returns></returns>
        public async Task Connect(string url, string channel_id, string user, string password)
        {
            User = user;
            ChannelId = channel_id;
            ServerUrl = CreateUri(url, channel_id, user, password); ;

            await ConnectWebsocket(ServerUrl, user, password);
            ShouldCloseListenerThread = false;
            StartListenerThread();
        }

        /// <summary>
        /// Disconnects from the currently connected Websocket server.
        /// </summary>
        /// <returns></returns>
        public async Task Disconnect()
        {
            ClearServerData();
            if (Socket == null) return;
            ShouldCloseListenerThread = true;
            if (ListenerThread != null)
            {
                ListenerThread.Join();
            }
            await Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }

        /// <summary>
        /// Sends a message with non-protocol payload to the Webscoket server.
        /// <br/>
        /// Use <c>SendText()</c> or <c>SendData()</c> to stay within the protocol standard and if you do not require a custom payload.
        /// </summary>
        /// <param name="msg_type">Can either be "message", "data" or any other non-standard string</param>
        /// <param name="payload">Payload to send alongside the message, make sure to represent the nested JSON objects as Dictionary(string, object)</param>
        /// <param name="to">Array of collab participants to send the message to. Will send to everyone if contains the string "all"</param>
        /// <returns></returns>
        public async Task Send(string msg_type, Dictionary<string, object> payload, string[]? to = null)
        {
            if (to == null)
            {
                to = ["all"];
            }

            Dictionary<string, object> proto = new Dictionary<string, object>()
            {
                { "version", 1 },
                { "type", msg_type },
                { "from", User },
                { "to", to },
                { "payload", payload }
            };

            string json = JsonSerializer.Serialize(proto);
            await SendString(Socket!, json);
        }

        /// <summary>
        /// Sends a text message to collab participants.
        /// <br/>
        /// The assumption is that the collab participants should respond to this message.
        /// </summary>
        /// <param name="sender">Name of the sender of the message (!= username) example: "Hilda" or "Meteora"</param>
        /// <param name="content">Content of the text message.</param>
        /// <param name="to">Array of collab participant usernames to send the message to. Will send to everyone if contains the string "all"</param>
        /// <returns></returns>
        public async Task SendText(string sender, string content, string[]? to = null)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>()
            {
                { "name", sender },
                { "content", content },
            };
            await Send("message", payload, to);
        }

        /// <summary>
        /// Sends a data message to collab participants.
        /// <br/>
        /// The assumption is that the collab participants should not respond to this message.
        /// <br/>
        /// Usage of the payload of this message depends on the agreed upon implementation.
        /// </summary>
        /// <param name="data_name"></param>
        /// <param name="data"></param>
        /// <param name="to"></param>
        /// <returns></returns>
        public async Task SendData(string data_name, string data, string[]? to = null)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>()
            {
                { "name", data_name },
                { "content", data },
            };
            await Send("message", payload, to);
        }

        /// <summary>
        /// Sends a non protocol-compliant json string.
        /// <br/>
        /// Do not use unless you know what you are doing, better protocol-compliant alternatives are <c>SendData()</c> and <c>Send()</c>.
        /// </summary>
        /// <param name="obj">JSON message to send, make sure to represent the nested JSON objects as Dictionary(string, object)</param>
        /// <returns></returns>
        public async Task SendNonProtocolJson(Dictionary<string, object> obj)
        {
            string json = JsonSerializer.Serialize(obj);
            await SendString(Socket!, json);
        }

        /// <summary>
        /// Adds an event listener for when the client receives a protocol-compliant message.
        /// <br/>
        /// Note that the message could be destined to another participant, or be from the user themselves.
        /// <br/>
        /// To only listen for incoming messages that you are the recipent of, use <c>OnTextMessage()</c> and <c>OnDataMessage()</c>
        /// </summary>
        /// <param name="listener">Async callback function</param>
        public void OnAllMessages(Func<ProtocolMessageUnknown, Task> listener)
        {
            ListenersAll.Add(listener);
        }

        /// <summary>
        /// Adds an event listener for when a text message is sent to you.
        /// <br/>
        /// The assumption is that you should respond to the incoming message.
        /// <br/>
        /// <c>message.payload.name</c> contains the name of the sender,
        /// <c>message.payload.content</c> contains the content of the message.
        /// </summary>
        /// <param name="listener">Async callback function</param>
        public void OnTextMessage(Func<ProtocolMessage, Task> listener)
        {
            ListenersText.Add(listener);
        }

        /// <summary>
        /// Adds an event listener for when a data message is sent to you.
        /// <br/>
        /// The assumption is that you should not respond to the incoming message.
        /// <br/>
        /// <c>message.payload.name</c> contains the label of the data,
        /// <c>message.payload.content</c> contains the data.
        /// </summary>
        /// <param name="listener">Async callback function</param>
        public void OnDataMessage(Func<ProtocolMessage, Task> listener)
        {
            ListenersData.Add(listener);
        }

        /// <summary>
        /// Adds an event listener for when a message with unknown type or non protocol-compliant payload is sent to you.
        /// </summary>
        /// <param name="listener">Async callback function</param>
        public void OnOtherMessage(Func<ProtocolMessageUnknown, Task> listener)
        {
            ListenersOther.Add(listener);
        }

        /// <summary>
        /// Adds an event listener for when a non protocol-compliant message is received.
        /// </summary>
        /// <param name="listener">Async callback function</param>
        public void OnNonProtocolMessage(Func<string, Task> listener)
        {
            ListenersNonProtocol.Add(listener);
        }

        /// <summary>
        /// Removes all listeners added via <c>OnSomething()</c> functions.
        /// </summary>
        public void RemoveAllListeners()
        {
            ListenersAll.Clear();
            ListenersText.Clear();
            ListenersData.Clear();
            ListenersOther.Clear();
            ListenersNonProtocol.Clear();
        }
    }
}
