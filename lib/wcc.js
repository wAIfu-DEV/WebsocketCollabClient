"use strict";

const ws = require("ws");

/**
 * Throws an error if condition is false.
 * @private
 * @param { boolean } condition
 * @param { string } message
 */
function assert(condition, message) {
  if (!condition) throw new Error(message);
}

class WebsocketCollabClient {
  //------------------------------------------------------------------------------
  // TYPEDEFS
  //------------------------------------------------------------------------------
  /** @typedef {{ user: string, pass: string }} UserPass */

  /** @typedef { "message" | "data" | "other" } ProtocolMessageType */

  /** @typedef {{ name: string, content: string }} Payload */

  /** @typedef {{ version: number, type: ProtocolMessageType, from: string, to: string[], payload: unknown  }} ProtocolMessageUnknown */
  /** @typedef {{ version: number, type: "message", from: string, to: string[], payload: Payload  }} ProtocolMessage */

  //------------------------------------------------------------------------------
  // MEMBERS
  //------------------------------------------------------------------------------
  /**
   * Version of the protocol used for the communication from client to client.
   * @constant
   * @readonly
   * @type { number }
   */
  PROTOCOL_VERSION = 1;

  /**
   * If the client is currently connected to the server.
   * @readonly
   * @type { boolean }
   */
  connected = false;

  /**
   * ID of the joined channel.
   * @readonly
   * @type { string }
   */
  channel_id = "";

  /**
   * Url of the joined server.
   * @readonly
   * @type { URL? }
   */
  server_url = null;

  /**
   * @private
   * @type { string }
   */
  user = "";

  /**
   * @private
   * @type { ws? }
   */
  socket = null;

  //------------------------------------------------------------------------------
  // PRIVATE METHODS
  //------------------------------------------------------------------------------
  /**
   * @private
   * @param { ws.ErrorEvent } ev
   */
  handleWsError(ev) {
    this.clearServerData();
    console.error("Collab websocket error:", ev.type, ev.message);
  }

  /**
   * @private
   * @param { ws.CloseEvent } ev
   */
  handleWsClose(ev) {
    this.clearServerData();
    if (!ev.wasClean) {
      console.warn("Lost connection to Collab Websocket, trying reconnect...");
      this.connectWebsocket(this.server_url);
      return;
    }
  }

  /**
   * @private
   * @param { ws.MessageEvent } ev
   */
  handleWsMessage(ev) {
    let data = ev.data.toString("utf8");

    /** @type { unknown | undefined } */
    let obj;
    try {
      obj = JSON.parse(data);
    } catch {
      return;
    }

    if (!this.isWellFormedProtocolMessage(obj)) {
      return;
    }

    /** @type { ProtocolMessageUnknown } */
    let proto_msg = obj;

    if (!this.isWellFormedMessagePayload(proto_msg.payload)) {
      return;
    }

    /** @type { ProtocolMessage } */
    let msg = proto_msg;

    if (!this.onAllMessages) return;
    this.onAllMessages(msg);

    if (!(msg.to.includes("all") || msg.to.includes(this.user))) {
      // Should not be received by user.
      return;
    }

    if (msg.from == this.user) {
      // Is from user
      return;
    }

    switch (msg.type) {
      case "message": {
        if (!this.onTextMessage) return;
        this.onTextMessage(msg.payload.name, msg.payload.content, msg);
        return;
      }
      case "data": {
        if (!this.onDataMessage) return;
        this.onDataMessage(msg.payload.name, msg.payload.data, msg);
        return;
      }
    }
  }

  /**
   * @private
   * @param { URL } url
   * @returns { Promise<void> }
   */
  connectWebsocket(url) {
    return new Promise((resolve, reject) => {
      let resolved = false;

      this.socket = new ws(url);

      this.socket.onerror = (err) => {
        console.trace(
          "Error while trying to connect to server:",
          err.type,
          err.message
        );
        this.clearServerData();
        if (resolved) return;
        resolved = true;
        reject(err.message);
      };

      this.socket.onopen = (ev) => {
        this.socket.onerror = this.handleWsError.bind(this);
        this.socket.onmessage = this.handleWsMessage.bind(this);
        this.socket.onclose = this.handleWsClose.bind(this);
        this.connected = true;
        if (resolved) return;
        resolved = true;
        resolve();
      };
    });
  }

  /**
   * @private
   * @param { unknown } obj
   * @returns { boolean }
   */
  isWellFormedProtocolMessage(obj) {
    if (obj.version == undefined || typeof obj.version != "number")
      return false;
    if (obj.type == undefined || typeof obj.type != "string") return false;
    if (obj.from == undefined || typeof obj.from != "string") return false;
    if (obj.to == undefined || typeof obj.to != "object") return false;
    if (obj.to.length == undefined) return false;
    if (obj.payload == undefined) return false;
    return true;
  }

  /**
   * @private
   * @param { unknown } payload
   * @returns { boolean }
   */
  isWellFormedMessagePayload(payload) {
    if (payload.name == undefined || typeof payload.name != "string")
      return false;
    if (payload.content == undefined || typeof payload.content != "string")
      return false;
    return true;
  }

  /**
   * @private
   */
  clearServerData() {
    this.user = "";
    this.channel_id = "";
    this.server_url = null;
    this.connected = false;
  }

  //------------------------------------------------------------------------------
  // PUBLIC METHODS
  //------------------------------------------------------------------------------
  /**
   * Connects to the server and joins the selected channel.
   *
   * Rejects if fails to establish connection to server.
   * @method
   * @function
   * @param { string | URL } url Url to the collab server.
   * @param { string } channel_id ID of the channel to join.
   * @param { UserPass } auth User and Password.
   * @returns { Promise<void> }
   */
  connect(url, channel_id, auth) {
    assert(url, "argument url cannot be undefined.");
    assert(channel_id, "argument channel_id cannot be undefined.");
    assert(auth, "argument auth cannot be undefined.");
    assert(auth.user, "argument auth.user cannot be undefined.");
    assert(auth.pass, "argument auth.pass cannot be undefined.");

    assert(
      url instanceof URL || typeof url == "string",
      "argument url must be of type string or URL."
    );
    assert(
      typeof channel_id == "string",
      "argument channel_id must be of type string."
    );
    assert(
      typeof auth.user == "string",
      "argument auth.user must be of type string."
    );
    assert(
      typeof auth.pass == "string",
      "argument auth.user must be of type string."
    );

    let uri;
    if (typeof url != "string") uri = url;
    else uri = new URL(url);

    uri.username = auth.user;
    uri.password = auth.pass;
    uri.pathname = `/${channel_id}`;

    this.user = auth.user;
    this.server_url = uri;
    this.channel_id = channel_id;

    return this.connectWebsocket(uri);
  }

  /**
   * Closes the connection to the server.
   * @method
   * @function
   */
  disconnect() {
    if (this.socket.readyState != ws.CLOSED) {
      this.socket.close();
    }
  }

  /**
   * Send a message to users of the channel, with the assumption that they sould
   * respond to said message.
   * @method
   * @function
   * @param { string } sender_name Name of the sender, for ex: "Hilda" or "Meteora"
   * @param { string } content Content of the text message.
   * @param { string[] } to Users to send the message to. If contains `"all"`,
   * will send message to all participants.
   */
  sendText(sender_name, content, to = ["all"]) {
    assert(
      sender_name != undefined && typeof sender_name == "string",
      "argument sender_name must be a string."
    );
    assert(
      content != undefined && typeof content == "string",
      "argument content must be a string."
    );
    assert(
      to != undefined && typeof to == "object",
      "argument to must be of type string array."
    );

    assert(to.length != undefined, "argument to must be of type string array.");
    for (let name of to) {
      assert(
        name != undefined && typeof name == "string",
        'in argument to: "' + name + '" must be of type string.'
      );
    }

    assert(
      this.socket != null && this.socket.readyState == ws.OPEN,
      "cannot send to collab, connection to server is closed."
    );

    /** @type { ProtocolMessage } */
    let obj = {
      version: 1,
      type: "message",
      from: this.user,
      to: to,
      payload: {
        name: sender_name,
        content: content,
      },
    };

    this.socket.send(JSON.stringify(obj));
  }

  /**
   * Send data to other participants, with the assumption that they may or may not
   * respond to receiving data.
   * @method
   * @function
   * @param { string } label Identifier of the sent data.
   * @param { string } data Sent data.
   * @param { string } to Users to send the message to. If contains `"all"`,
   * will send message to all participants.
   */
  sendData(label, data, to = ["all"]) {
    assert(
      label != undefined && typeof label == "string",
      "argument label must be a string."
    );
    assert(
      data != undefined && typeof data == "string",
      "argument data must be a string."
    );
    assert(
      to != undefined && typeof to == "object",
      "argument to must be of type string array."
    );

    assert(to.length != undefined, "argument to must be of type string array.");
    for (let name of to) {
      assert(
        name != undefined && typeof name == "string",
        'in argument to: "' + name + '" must be of type string.'
      );
    }

    assert(
      this.socket != null && this.socket.readyState == ws.OPEN,
      "cannot send to collab, connection to server is closed."
    );

    /** @type { ProtocolMessage } */
    let obj = {
      version: 1,
      type: "data",
      from: this.user,
      to: to,
      payload: {
        name: label,
        content: data,
      },
    };

    this.socket.send(JSON.stringify(obj));
  }

  /**
   * Called when a protocol message is received from the server.
   *
   * Is called before `onTextMessage` and `onDataMessage`, so they might also
   * get called for the same message.
   *
   * Only use if you want to handle specific cases.
   *
   * Use `onMessage` instead if you are only interested with the text messages.
   * @member
   * @type { ((json: ProtocolMessage) => void)? }
   */
  onAllMessages = null;

  /**
   * Called when a text message is received from the server.
   *
   * The assumption is that you will respond to this message.
   * @method
   * @type { ((sender: string, content: string, raw_json: ProtocolMessage) => void)? }
   */
  onTextMessage = null;

  /**
   * Called when a data message is received from the server.
   *
   * The assumption is that you will not respond to this message.
   * @method
   * @type { ((label: string, data: string, raw_json: ProtocolMessage) => void)? }
   */
  onDataMessage = null;

  removeAllListeners() {
    this.onAllMessages = null;
    this.onTextMessage = null;
    this.onDataMessage = null;
  }
}

module.exports = WebsocketCollabClient;
