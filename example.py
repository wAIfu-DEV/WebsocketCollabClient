import time
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
    Called when a message destined to you, and that you did not send is received
    by the client.
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

while True:
    time.sleep(2)
    client.send_text("Hilda", "This is a test message", ["all"])
