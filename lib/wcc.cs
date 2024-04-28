using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebsocketCollab
{
    public class ProtocolMessage
    {
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        /// <summary>
        /// Type of message, usually "message" or "data", but can also be any other non protocol-compliant string.
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        /// <summary>
        /// Username of the message sender, this is not the name of the AI vtuber.
        /// </summary>
        [JsonPropertyName("from")]
        public string From { get; set; }

        [JsonPropertyName("to")]
        public List<string> To { get; set; }

        [JsonPropertyName("payload")]
        public Payload Payload { get; set; }
    }

    public class Payload
    {
        /// <summary>
        /// Name of the AI Vtuber that sent the message, or label of the data depending on message type.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// Content of the message, or data depending on message type.
        /// </summary>
        [JsonPropertyName("content")]
        public string Content { get; set; }
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
        private bool ShouldCloseListenerThread = false;
        private Task? task;

        public EventHandler<ProtocolMessage> All { get; set; }
        public EventHandler<ProtocolMessage> Text { get; set; }
        public EventHandler<ProtocolMessage> Data { get; set; }

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
            string reconstructed_url = $"{uri.Scheme}://{user}:{password}@{uri.Host}"; 
            if (uri.Port != 80)
            {
                reconstructed_url += ":" + uri.Port.ToString();
            }

            reconstructed_url += $"/{channel_id}";

            return new Uri(reconstructed_url);
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

                var data = JsonSerializer.Deserialize<ProtocolMessage>(received);

                All?.Invoke(this, data);

                if (!(data.To.Contains("all") || data.To.Contains(User)))
                {
                    continue;
                };

                if (data.From == User)
                {
                    continue;
                };

                switch (data.Type)
                {
                    case "message":
                        Text?.Invoke(this, data);
                        break;
                    case "data":
                        Data?.Invoke(this, data);
                        break;
                    default:
                        break;
                }
            }
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

            // start watcher task
            task = Task.Run(Listener);
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
        public async Task Send(string msg_type, Payload payload, string[]? to = null)
        {
            if (to == null)
                to = ["all"];

            var msg = new ProtocolMessage()
            {
                Version = 1,
                Type = msg_type,
                From = User,
                To = to?.ToList(),
                Payload = payload
            };
            string json = JsonSerializer.Serialize(msg);
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
            var payload = new Payload()
            {
                Name = sender,
                Content = content,
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
            var payload = new Payload()
            {
                Name = data_name,
                Content = data,
            };
            await Send("data", payload, to);
        }    
    }
}
