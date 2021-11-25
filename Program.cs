using System;
using static Networking;

namespace Carcassonne_Test_Networking
{
    class Program
    {
        const string client_password = "666";
        const string server_password = "777";
        public class RelayServerTestHandles : IRelayServerHandlers
        {
            public void FailHandle(NetException ex)
            {
                throw new NotImplementedException();
            }
        }
        public class RelayClientTestHandles : IRelayClientHandlers
        {
            public void FailHandle(NetException ex)
            {
                throw new NotImplementedException();
            }

            public void OnDisconnect(Peer.Connection con, DisconnectReason reason)
            {
                throw new NotImplementedException();
            }

            public void OnLoginFailure()
            {
                throw new NotImplementedException();
            }

            public void OnLoginSuccess(Peer.Connection con)
            {
                throw new NotImplementedException();
            }

            public void OnPeerDiscovered(Peer.Connection con, bool isadmin)
            {
                throw new NotImplementedException();
            }

            public void OnReceived(Message msg)
            {
                throw new NotImplementedException();
            }

            public void OnStatus(MStatus stat)
            {
                throw new NotImplementedException();
            }
        }
        static void Main(string[] args)
        {
            Console.WriteLine("creating relayserver!");
            var relay = new RelayServer(new RelayServerTestHandles(), 6666, client_password, server_password);
            Console.WriteLine("starting");
            relay.Start();
            Console.WriteLine("Hello World!");
        }
    }
}
