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

  while (true) {
    await new Promise((resolve) => setTimeout(resolve, 2000));
    client.sendText("Hilda", "This is a test message", ["all"]);
  }
}

main();
