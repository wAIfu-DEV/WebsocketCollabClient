const WebsocketCollabClient = require("./lib/wcc");

const WS_URL = "<url>";
const USER = "<user>";
const PASS = "<pass>";
const CHANNEL_ID = "<channel id>";

async function main() {
  let client = new WebsocketCollabClient();
  await client.connect(WS_URL, CHANNEL_ID, { user: USER, pass: PASS });

  client.onTextMessage = (sender, content, json) => {
    console.log("RAW:", json);
    console.log(`From: '${sender}' Message: '${content}'`);
  };

  client.sendText("Hilda", "This is a test message", ["all"]);
}

main();
