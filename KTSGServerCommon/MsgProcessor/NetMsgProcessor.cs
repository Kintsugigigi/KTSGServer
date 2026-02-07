using System;
using System.Collections.Generic;
using System.Reflection;
using Google.Protobuf;
using KTSG.Core;      // 引用 SimplePool 所在的命名空间
using KTSG.Proto; 

namespace KTSG.Network
{
    public class NetMsgProcessor
    {
        public static NetMsgProcessor Instance { get; } = new NetMsgProcessor();
        private NetMsgProcessor() { }
        
        private readonly Dictionary<uint, IMsgHandler> _handlers = new Dictionary<uint, IMsgHandler>();
        
        private readonly Dictionary<ushort, RpcRequest> _rpcRequests = new Dictionary<ushort, RpcRequest>();

        private SimplePool<RpcRequest> _rpcPool;
        private bool _isInit = false;

        public void Init(Assembly assembly, int rpcPoolSize = 50)
        {
            if (_isInit) return;
            
            _rpcPool = new SimplePool<RpcRequest>(
                factory: () => new RpcRequest(),
                onGet: null,
                onReturn: req => req.Reset(),
                maxCapacity: rpcPoolSize
            );
            
            ScanHandlers(assembly);
            _isInit = true;
        }
        

        public void ProcessMessage(INetConnection conn, uint cmdId, ushort rpcSeq, ReadOnlySpan<byte> bodyData)
        {
            if (!MsgMeta.CmdIdToItem.TryGetValue(cmdId, out var meta)) return;

            try
            {
                IMessage msg = meta.Parser.ParseFrom(bodyData);
                Dispatch(conn, meta, rpcSeq, msg);
            }
            catch (Exception ex)
            {
                MyLog.Error($"[Net] Parse Error: {ex.Message}");
            }
        }

        private void Dispatch(INetConnection conn, MsgMeta.MsgMetaItem meta, ushort rpcSeq, IMessage msg)
        {
            if (_handlers.TryGetValue(meta.CmdId, out var handler))
            {
                handler.Handle(conn, msg, rpcSeq);
            }
            else
            {
                MyLog.Debug($"[Net] No Handler for CmdId:{meta.CmdId}");
            }
        }
        

        public RpcRequest AddRpcRequest(ushort seq, uint rpcId,long timeoutMs = 5000,Action onComplete = null, Action onRelease = null)
        {
            if (!MsgMeta.CmdIdToItem.TryGetValue(rpcId, out var meta))
            {
                MyLog.Error($"[Net] AddRpcRequest 失败: 未知的消息 ID {rpcId}");
                return null;
            }
            if (meta.ResponseId == 0)
            {
                MyLog.Error($"[Net] AddRpcRequest 失败: 消息 {rpcId} 不是一个有效的 RPC 请求 (未配置响应包)");
                return null;
            }
            var req = _rpcPool.Get();
            req.Init(meta.ResponseId, timeoutMs);
            req.OnComplete = onComplete;
            req.OnRelease = onRelease;
            _rpcRequests[seq] = req;
            return req;
        }
        
        public bool InvokeRpcCallback(ushort rpcSeq, uint rspCmdId, IMessage msg)
        {
            if (_rpcRequests.Remove(rpcSeq, out var req))
            {
                if (req.RspId != rspCmdId)
                {
                    MyLog.Error($"[Net] RPC Mismatch: Seq:{rpcSeq}, Expected:{req.RspId}, Got:{rspCmdId}");
                    _rpcPool.Return(req); 
                    return false;
                }
                if (req.Status != RpcRequest.RpcState.Pending)
                {
                    MyLog.Warning($"[Net] RPC Seq:{rpcSeq} found but state is {req.Status} (not Pending). Ignored.");
                    _rpcPool.Return(req);
                    return false;
                }
                req.SetResult(msg);
                _rpcPool.Return(req);
                return true;
            }
            else
            {
                MyLog.Warning($"[Net] RPC Request not found or timeout. Seq:{rpcSeq}");
                return false;
            }
        }

        public void Update()
        {
            if (_rpcRequests.Count == 0) return;

            long now = DateTime.UtcNow.Ticks;
            List<ushort> toRemove = null;
            
            foreach (var kv in _rpcRequests)
            {
                if (kv.Value.CheckTimeout(now))
                {
                    toRemove ??= new List<ushort>();
                    toRemove.Add(kv.Key);
                }
            }
            
            if (toRemove != null)
            {
                foreach (var seq in toRemove)
                {
                    if (_rpcRequests.Remove(seq, out var req))
                    {
                        _rpcPool.Return(req);
                    }
                }
            }
        }


        public bool TryGetCmdId(Type msgType, out uint cmdId)
        {
            if (MsgMeta.TypeToItem.TryGetValue(msgType, out var meta))
            {
                cmdId = meta.CmdId;
                return true;
            }
            cmdId = 0;
            return false;
        }

        private void ScanHandlers(Assembly assembly)
        {
            _handlers.Clear();
            var interfaceType = typeof(IMsgHandler);

            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface) continue;
                if (!interfaceType.IsAssignableFrom(type)) continue;

                try
                {
                    Type msgType = GetMsgTypeFromHandler(type);
                    if (msgType != null && MsgMeta.TypeToItem.TryGetValue(msgType, out var meta))
                    {
                        if (!_handlers.ContainsKey(meta.CmdId))
                        {
                            var handler = (IMsgHandler)Activator.CreateInstance(type);
                            _handlers.Add(meta.CmdId, handler);
                        }
                        else
                        {
                            MyLog.Error($"[Net] Duplicate Handler for {msgType.Name}");
                        }
                    }
                }
                catch (Exception e)
                {
                    MyLog.Error($"[Net] Handler Load Error: {type.Name} - {e.Message}");
                }
            }
        }

        private Type GetMsgTypeFromHandler(Type handlerType)
        {
            var baseType = handlerType.BaseType;
            while (baseType != null)
            {
                if (baseType.IsGenericType)
                {
                    var def = baseType.GetGenericTypeDefinition();
                    if (def == typeof(MsgHandler<>)) return baseType.GetGenericArguments()[0];
                    if (def == typeof(ReqHandler<,>)) return baseType.GetGenericArguments()[0];
                }
                baseType = baseType.BaseType;
            }
            return null;
        }
    }
}