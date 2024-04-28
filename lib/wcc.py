import urllib3.util
import json
import threading

from typing import Any, Callable

import websockets
import websockets.sync
import websockets.sync.client

class Payload:
    name: str
    content: str

    def __init__(self,  name: str, content: str) -> None:
        self.name = name
        self.content = content

    def to_dict(self)-> dict:
        return {
            "name": self.name,
            "content": self.content
        }



class ProtocolMessageUnknown:
    version: int
    type: str
    sender: str
    to: list[str]
    payload: Any

    def __init__(self, version: int, type: str, sender: str, to: list[str], payload: Any ) -> None:
        self.version = version
        self.type = type
        self.sender = sender
        self.to = to
        self.payload = payload
    
    def to_dict(self)-> dict:
        return {
            "version": self.version,
            "type": self.type,
            "from": self.sender,
            "to": self.to,
            "payload": self.payload
        }



class ProtocolMessage:
    version: int
    type: str
    sender: str
    to: list[str]
    payload: Payload

    def __init__(self, version: int, type: str, sender: str, to: list[str], payload: Payload ) -> None:
        self.version = version
        self.type = type
        self.sender = sender
        self.to = to
        self.payload = payload
    
    def to_dict(self)-> dict:
        return {
            "version": self.version,
            "type": self.type,
            "from": self.sender,
            "to": self.to,
            "payload": self.payload.to_dict()
        }



class WebsocketCollabClient:

    PROTOCOL_VERSION: int = 1
    """
    Version of the protocol used for the communication from client to client.
    """

    connected: bool = False
    """
    If the client is currently connected to the websocket server.
    """

    channel_id: str = ""
    """
    ID of the channel the client is currently connected to.
    """

    server_url: str = ""
    """
    URL of the server the client is currently connected to.
    """

    __user: str = ""
    __socket: websockets.sync.client.ClientConnection | None = None
    __thread: threading.Thread | None = None
    __should_close_thread: bool = False

    __listeners_all_msg: list[Callable[[ProtocolMessage], None]] = []
    __listeners_text_msg: list[Callable[[ProtocolMessage], None]] = []
    __listeners_data_msg: list[Callable[[ProtocolMessage], None]] = []


    def __connect_websocket(self, url: str)-> None:
        self.__socket = websockets.sync.client.connect(url)
        self.connected = True
    

    def __clear_server_data(self)-> None:
        self.__user = ""
        self.channel_id = ""
        self.server_url = ""
        self.connected = False


    def __is_well_formed_protocol_message(self, obj: dict)-> bool:
        if "version" not in obj or type(obj["version"]).__name__ != "int":
            return False
        if "type" not in obj or type(obj["type"]).__name__ != "str":
            return False
        if "from" not in obj or type(obj["from"]).__name__ != "str":
            return False
        if "to" not in obj or type(obj["to"]).__name__ != "list":
            return False
        if "payload" not in obj:
            return False
        return True


    def __is_well_formed_payload(self, obj: dict)-> bool:
        if "name" not in obj or type(obj["name"]).__name__ != "str":
            return False
        if "content" not in obj or type(obj["content"]).__name__ != "str":
            return False
        return True


    def __call_listeners(self, listeners: Callable, arg: Any)-> None:
        for listener in listeners:
            listener(arg)


    def __listener(self)-> None:
        while not self.__should_close_thread:
            received: str
            try:
                received = str(self.__socket.recv())
            except:
                return

            obj: dict
            try:
                obj = json.loads(received)
            except:
                return
            
            if not self.__is_well_formed_protocol_message(obj):
                return

            proto_msg: ProtocolMessageUnknown = ProtocolMessageUnknown(
                version=obj["version"],
                type=obj["type"],
                sender=obj["from"],
                to=obj["to"],
                payload=obj["payload"]
            )

            if not self.__is_well_formed_payload(proto_msg.payload):
                return
            
            msg: ProtocolMessage = proto_msg

            self.__call_listeners(self.__listeners_all_msg, msg)

            if not ("all" in msg.to or self.__user in msg.to):
                return

            if msg.sender == self.__user:
                return

            match msg.type:
                case "message":
                    self.__call_listeners(self.__listeners_text_msg, msg)
                    return
                case "data":
                    self.__call_listeners(self.__listeners_data_msg, msg)
                    return
                
    
    def __start_listener_thread(self)-> None:
        self.__thread = threading.Thread(target=self.__listener, args=[])
        self.__thread.start()


    def connect(self, url: str | urllib3.util.Url, channel_id: str, user: str, password: str):
        """
        Connects to the server and joins the selected channel.
        """
        assert url != None
        assert channel_id != None
        assert user != None
        assert password != None

        assert type(url).__name__ == "str" or type(url).__name__ == "Url"
        assert type(channel_id).__name__ == "str"
        assert type(user).__name__ == "str"
        assert type(password).__name__ == "str"

        uri: urllib3.util.Url
        if (type(url).__name__ == "str"):
            uri = urllib3.util.parse_url(url)
        else:
            uri = url
        
        reconstructed_uri: str = ""

        if uri.scheme is not None:
            reconstructed_uri += uri.scheme + "://"
        reconstructed_uri += f"{user}:{password}" + "@"
        if uri.host is not None:
            reconstructed_uri += uri.host
        if uri.port is not None:
            reconstructed_uri += ":" + str(uri.port)
        reconstructed_uri += f"/{channel_id}"

        self.__user = user
        self.channel_id = channel_id
        self.server_url = reconstructed_uri
        
        self.__connect_websocket(reconstructed_uri)
        self.__start_listener_thread()
    

    def disconnect(self)-> None:
        """
        Closes the connection to the server.
        """
        self.__clear_server_data()
        if self.__socket == None: return
        self.__should_close_thread = True
        self.__thread.join()
        self.__socket.close()
    

    def __send(self, msg_type: str, payload: dict, to: list[str] | None = None)-> None:
        assert msg_type != None
        assert payload != None

        if to == None: to = ["all"]
        
        assert type(msg_type).__name__ == "str"
        assert type(payload).__name__ == "dict"
        assert type(to).__name__ == "list"

        assert self.__socket != None

        obj = {
            "version": 1,
            "type": "message",
            "from": self.__user,
            "to": to,
            "payload": payload,
        }

        self.__socket.send(json.dumps(obj))
    

    def send_text(self, sender_name: str, content: str, to: list[str] | None = None)-> None:
        """
        Send a message to users of the channel, with the assumption that they
        should respond to said message.
        """
        self.__send("message", {
            "name": sender_name,
            "content": content
        }, to)


    def send_data(self, data_name: str, data: str, to: list[str] | None = None)-> None:
        """
        Send data to other participants, with the assumption that they will not
        respond to receiving the data.
        """
        self.__send("data", {
            "name": data_name,
            "content": data
        }, to)
    

    def on_all_messages(self, listener: Callable[[ProtocolMessage], None])-> None:
        """
        Called when a protocol message is received from the server.

        Is called before `on_text_message` and `on_data_message`, so they might
        also get called for the exact same message.

        Only use if you want to handle specific cases.

        Use `on_text_message` instead if you are only interested with the text
        messages.
        """
        self.__listeners_all_msg.append(listener)
    

    def on_text_message(self, listener: Callable[[ProtocolMessage], None])-> None:
        """
        Called when a text message is received from the server.

        The assumption is that you will respond to this message.
        """
        self.__listeners_text_msg.append(listener)
    

    def on_data_message(self, listener: Callable[[ProtocolMessage], None])-> None:
        """
        Called when a data message is received from the server.

        The assumption is that you will not respond to this message.
        """
        self.__listeners_data_msg.append(listener)


    def remove_all_listeners(self)-> None:
        """
        Remove all listeners added via `on_#_message` functions.
        """
        self.__listeners_all_msg.clear()
        self.__listeners_text_msg.clear()
        self.__listeners_data_msg.clear()