# Websocket Collab Client

This project is related to AWAS's Websocket collab server: https://github.com/AWAS666/WebsocketCollabServer

The goal of this project is to provide a unified and easy-to-use library in order to handle communications between parties during a livestreamed collab between 2 or more AI Vtubers.

## What the library does

- Allows sending to single, multiple or all collab partners on the webscoket collab server channel.

## What it doesn't do

- Orchestration of the timings, the assumption is that the text messages would be sent at the very end of playing the TTS.

## Supported languages

- Javascript/TypeScript
- Python
- C#

New languages can easily be added in the future.

## Usage

#### Those are only examples making use of the functions provided by the library. Your implementation will and should be specific to the architecture of the AI Vtuber.

Python:

```python
from lib.wcc import WebsocketCollabClient, ProtocolMessage, ProtocolMessageUnknown

WS_URL = "<url>"
USER = "<user>"
PASS = "<pass>"
CHANNEL_ID = "<channel id>"

client = WebsocketCollabClient()
client.connect(
    url=WS_URL,
    channel_id=CHANNEL_ID,
    user=USER,
    password=PASS)

def listener_text(msg: ProtocolMessage):
    print("RAW:", msg.to_dict())
    print(f"From: '{msg.payload.name}' Message: '{msg.payload.content}'")

client.on_text_message(listener_text)

client.send_text("Hilda", "This is a test message", ["all"])
```

JavaScript:

```js
const WebsocketCollabClient = require("./lib/wcc");

const WS_URL = "<url>";
const USER = "<user>";
const PASS = "<pass>";
const CHANNEL_ID = "<channel id>";

async function main() {
  let client = new WebsocketCollabClient();
  await client.connect(WS_URL, CHANNEL_ID, { user: USER, pass: PASS });

  client.onTextMessage = (sender, content, raw) => {
    console.log("RAW:", msg.to_dict());
    console.log(`From: '${sender}' Message: '${content}'`);
  };

  client.sendText("Hilda", "This is a test message", ["all"]);
}

main();
```

C#:

```c#
using WebsocketCollab;

const string WS_URL = "<url>";
const string USER = "<user>";
const string PASS = "<pass>";
const string CHANNEL_ID = "<channel id>";

WebsocketCollabClient wcc = new WebsocketCollabClient();
await wcc.Connect(WS_URL, CHANNEL_ID, USER, PASS);

wcc.OnTextMessage(async (ProtocolMessage message) =>
{
    Console.WriteLine($"RAW: {message}");
    Console.WriteLine($"From: '{message.payload.name}' Message: '{message.payload.content}'");
});

await wcc.SendText("Hilda", "This is a test message", ["all"]);

while (true) { };
```

## How to setup

1. Copy the right version of the library to your project.
2. If using Python, use `pip install websockets==12.0`
3. If using NodeJS, use `npm install ws@8.17.0`

### Format of JSON messages

```json
{
  "version": 1,
  "type": "message" | "data",
  "from": "<user>",
  "to": ["all" | "<user>", ...],
  "payload": {
    "name": "Hilda",
    "data": "This is a test message."
  }
}
```
