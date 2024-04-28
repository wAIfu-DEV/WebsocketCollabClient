using WebsocketCollab;

const string WS_URL = "<url>";
const string USER = "<user>";
const string PASS = "<pass>";
const string CHANNEL_ID = "<channel id>";

WebsocketCollabClient wcc = new WebsocketCollabClient();
await wcc.Connect(WS_URL, CHANNEL_ID, USER, PASS);

wcc.OnAllMessages(async (ProtocolMessageUnknown message) =>
{
    Console.WriteLine($"RAW: {message}");
});

wcc.OnTextMessage(async (ProtocolMessage message) =>
{
    Console.WriteLine($"STR: {message}");
    Console.WriteLine($"From: '{message.payload.name}' Message: '{message.payload.content}'");
});

await wcc.SendText("Hilda", "This is a test message", ["all"]);

while (true) { };