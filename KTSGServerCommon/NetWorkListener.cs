using System;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using Google.Protobuf; // 引入 Protobuf
using KTSG.Network;  // 引入你的命名空间

namespace KTSG.Network
{
    public class NetworkListener : INetEventListener
    {
        // 1. 当 Peer 成功连接
        public void OnPeerConnected(NetPeer peer)
        {
            MyLog.Info($"[Network] 已连接到对端: {peer.Address},{peer.Port}");
            // 如果你有 Session 管理，可以在这里创建并绑定到 Tag
            // peer.Tag = new Session(peer); 
        }

        // 2. 当 Peer 断开连接
        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            MyLog.Warning($"[Network] 对端断开: {peer.Address},{peer.Port}, 原因: {disconnectInfo.Reason}");
            // 在这里清理 Session 资源
        }

        // 3. 【核心】接收到业务数据
        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            try
            {
                // --- 第一步：解析自定义协议头 ---
                // 假设你的协议头是：[4字节 CmdId] + [4字节 RpcSeq]
                if (reader.AvailableBytes < 8) 
                {
                    MyLog.Error("收到非法短包");
                    return;
                }

                uint cmdId = reader.GetUInt();
                uint rpcSeq = reader.GetUInt();

                // --- 第二步：Protobuf 反序列化 ---
                // 注意：在这里必须把数据从 reader 读出来，因为 reader 离开此方法会被 Recycle
                IMessage msg = KiraraNetwork.MsgMeta.Deserialize(cmdId, reader);

                // --- 第三步：分发到业务异步队列 ---
                Session session = peer.Tag as Session;
                NetMsgProcessor.Instance.Enqueue(session, cmdId, rpcSeq, msg);
            }
            catch (Exception ex)
            {
                MyLog.Error(ex, "解析消息逻辑异常");
            }
        }

        // 4. 收到非连接的 UDP 消息（如服务器发现）
        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
            MyLog.Debug($"收到来自 {remoteEndPoint} 的未连接消息，类型: {messageType}");
        }

        // 5. 延迟（Ping）更新
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
            // 可以同步到 Session 中展示在 UI 上
        }

        // 6. 收到连接请求（仅服务端会走这里）
        public void OnConnectionRequest(ConnectionRequest request)
        {
            // 如果连接数没满，或者握手 Key 正确，则接受
            // request.Accept(); 
            // 否则拒绝
            // request.Reject();
        }

        // 7. 当可靠包确认送达（可选钩子）
        public void OnMessageDelivered(NetPeer peer, object userData)
        {
            // 可以在发送时附带一个 ID，到这里确认“玩家确实收到了这个重要的任务通知”
        }

        // 8. 错误处理
        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            MyLog.Error($"网络 Socket 错误: {socketError}, 端点: {endPoint}");
        }
    }
}