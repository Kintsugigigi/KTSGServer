using System;

namespace KTSG.Network
{
    public class TCPServerManager
    {
        private TCPNetManager _manager;
        
        public bool Start(string bindAddress, int port)
        {
            if (_manager != null)
            {
                return true;
            }

            _manager = new TCPNetManager();
            if (!_manager.StartServer(bindAddress, port))
            {
                _manager = null;
                return false;
            }

            return true;
        }

        public void SetClientCallbacks(
            Action<INetConnection>? onClientConnected,
            Action<INetConnection>? onClientDisconnected)
        {
            if (_manager == null)
            {
                throw new InvalidOperationException("TCP server is not started.");
            }

            _manager.SetClientCallbacks(onClientConnected, onClientDisconnected);
        }

        public void Update()
        {
            _manager?.PollEvents();
        }

        public void Stop()
        {
            if (_manager == null)
            {
                return;
            }

            _manager.Stop();
            _manager = null;
        }
    }
}
