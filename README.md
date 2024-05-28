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

#### Those are only examples making use of some of the functions provided by the library. Your implementation will and should be specific to the architecture of the AI Vtuber.

Python:

```python
from lib.wcc import WebsocketCollabClient, ProtocolMessage

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
    """
    Called when a message you did not send is received by the client.
    """
    print("RAW:", msg.to_dict())
    print(f"From: '{msg.payload.name}' Message: '{msg.payload.content}'")

def listener_all(msg: ProtocolMessage):
    """
    Called with every message, even those you sent or those that are not destined
    to you. Additional checks may be required.
    """
    print("Received:", msg.to_dict())

client.on_text_message(listener_text)
client.on_all_messages(listener_all)

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

  // Called when a message destined to you, and that you did not send is received by the client.
  client.onTextMessage = (sender, content, json) => {
    console.log("RAW:", json);
    console.log(`From: '${sender}' Message: '${content}'`);
  };

  // Called with every message, even those you sent or those that are not destined to you. Additional checks may be required.
  client.onAllMessages = (json) => {
    console.log("Received:", json);
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

// Called when a message destined to you, and that you did not send is received by the client.
wcc.OnTextMessage += (s, msg) =>
{
    Console.WriteLine($"From: '{msg.Payload.Name}' Message: '{msg.Payload.Content}'");
};

// Called with every message, even those you sent or those that are not destined to you. Additional checks may be required.
wcc.OnAllMessages += (s, msg) =>
{
    Console.WriteLine($"From: '{msg.Payload.Name}' Message: '{msg.Payload.Content}'");
};

await wcc.SendText("Hilda", "This is a test message", ["all"]);
```

## How to setup

1. Copy the right version of the library to your project.
2. If using Python, use `pip install websockets==12.0`
3. If using NodeJS, use `npm install ws@8.17.0`

### Format of JSON messages

```json
{
  "version": 1,
  "type": "message" /* "message" | "data" */,
  "from": "<user>",
  "to": ["all"] /* ["all"] | ["<user>", ...] */,
  "payload": {
    "name": "Hilda",
    "content": "This is a test message."
  }
}
```

- **version**: Version of protocol message.
- **type**: Type of message, can be either "message" or "data". "message" indicates that the message is the LLM output of another ai vtuber. "data" indicates that the payload should be handled differently, depending on the name of the payload.
- **from**: Collab username of the sender.
- **to**: Array of collab usernames to send the message to. If contains "all", will send to all collab partners.
- **payload**: The contents of the message.
- **payload.name**: Depends on message type. For "message": `payload.name` is the name of the AI vtuber. For "data": `payload.name` is the label of the data.
- **payload.content**: Depends on message type. For "message": `payload.content` is the output of the LLM in string format. For "data": Undefined, can either be plain string or stringified JSON.
