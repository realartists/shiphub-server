// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.md in the project root for license information.

using System.Net.WebSockets;

namespace RealArtists.ShipHub.Common.WebSockets {
  internal sealed class WebSocketMessage {
    public static readonly WebSocketMessage EmptyTextMessage = new WebSocketMessage(string.Empty, WebSocketMessageType.Text);
    public static readonly WebSocketMessage EmptyBinaryMessage = new WebSocketMessage(new byte[0], WebSocketMessageType.Binary);
    public static readonly WebSocketMessage CloseMessage = new WebSocketMessage(null, WebSocketMessageType.Close);

    public readonly object Data;
    public readonly WebSocketMessageType MessageType;

    public WebSocketMessage(object data, WebSocketMessageType messageType) {
      Data = data;
      MessageType = messageType;
    }
  }
}
