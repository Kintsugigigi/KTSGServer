using System;
using Google.Protobuf;

namespace KTSG.Network
{
    public class RpcRequest
    {
        public enum RpcState
        {
            None = 0,
            Pending = 1,
            Completed = 2,
            Timeout = 3,
        }
        
        public uint RspId { get; private set; }
        public RpcState Status { get; private set; }
        
        private IMessage _msg;
        
        public Action OnComplete;
        public Action OnRelease;

        private long _expireTime;


        public void Init(uint rspId, long timeoutMs)
        {
            RspId = rspId;
            Status = RpcState.Pending;
            _msg = null;
            _expireTime = DateTime.UtcNow.Ticks + (timeoutMs * 10000);
        }


        public T GetMsg<T>() where T : class, IMessage
        {
            return _msg as T;
        }
        
        public void SetResult(IMessage msg)
        {
            if (Status != RpcState.Pending) return;
            _msg = msg;
            Status = RpcState.Completed;
            OnComplete?.Invoke();
        }
        
        public void Release()
        {
            if (Status != RpcState.Pending) return;
            Status = RpcState.Timeout;
            OnRelease?.Invoke();
        }


        public bool CheckTimeout(long nowTicks)
        {
            if (Status != RpcState.Pending) return false;

            if (nowTicks > _expireTime)
            {
                Release();
                return true;
            }
            return false;
        }


        public void Reset()
        {
            RspId = 0;
            Status = RpcState.None;
            _msg = null;
            OnComplete = null;
            OnRelease = null;
            _expireTime = 0;
        }
    }
}