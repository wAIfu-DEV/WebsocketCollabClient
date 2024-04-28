using WebsocketCollab;

const string WS_URL = "<url>";
const string USER = "<user>";
const string PASS = "<pass>";
const string CHANNEL_ID = "<channel id>";

WebsocketCollabClient wcc = new WebsocketCollabClient();
await wcc.Connect(WS_URL, CHANNEL_ID, USER, PASS);
wcc.All += (s, e) => Console.WriteLine($"RAW: {e.Payload.Content}");
wcc.Text += (s, e) =>
{
    Console.WriteLine($"From: '{e.Payload.Name}' Message: '{e.Payload.Content}'");
};


await wcc.SendText("Hilda", "This is a test message", ["all"]);

while (true) { };