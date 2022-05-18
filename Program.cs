#define DISABLE_TIMEOUTS
#define ASSERT_NET_WARNINGS
#define ASSERT_NET_ERRORS
#define ASSERT_NET_FATALS

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

using Newtonsoft.Json;
using static System.Math;
using static Networking;
using static Utils;
using System.Net.NetworkInformation;

namespace Carcassonne_Test_Networking
{
    class Program
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RegisterPlayerRequest
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_NAME_LENGTH)]
            public string name;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct RegisterPlayerAnswer
        {
            bool Success;
            int PlayerID;
        }
        public class TestServer : NetworkedStateMachine
        {
            
            protected override void Logic()
            {
                AwaitConnection().Wait();
                Console.WriteLine("Server connected");
                


            }
            protected override void OnConnectionLost(NetConnectionErrorException ex)
            {
                throw new NotImplementedException();
            }
            public TestServer(IPEndPoint relay) : base(relay)
            {

            }
        }
        public class TestPlayer : NetworkedStateMachine
        {
            public enum NetPlayerState
            {
                ERROR=0,
                DISCONNECTED,
                CONNECTING,
                RECEIVING_INITIAL_STATE,
                LOBBY,
                LOADING,
                SYNCHRONIZING,
                MAKE_MOVE,
                AWAIT_MOVE,
            }
            public Action<NetPlayerState> OnStateChanged = nps => {};
            NetPlayerState _state = NetPlayerState.ERROR;
            public NetPlayerState State
            {
                get => _state;
                protected set 
                {
                    if(_state != value)
                    {
                        OnStateChanged(value);
                        Console.WriteLine($"State changed from {_state} to {value}");
                    }
                    _state = value;
                }
            }
            protected override void Logic()
            {
                State = NetPlayerState.CONNECTING;
                AwaitConnection().Wait();
                Console.WriteLine("Client connected");
                
                State = NetPlayerState.RECEIVING_INITIAL_STATE;
            }

            protected override void OnConnectionLost(NetConnectionErrorException ex)
            {
                State = NetPlayerState.DISCONNECTED;
            }

            public TestPlayer(IPEndPoint relay) : base(relay)
            {

            }
        }
        public static ushort GetFreePort(int startPort)
        {
            int portStartIndex = startPort;
            int count = 99;
            IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();
            IPEndPoint[] udpEndPoints = properties.GetActiveUdpListeners();

            List<int> usedPorts = udpEndPoints.Select(p => p.Port).ToList<int>();
            int unusedPort = 0;

            unusedPort = Enumerable.Range(portStartIndex, 99).Where(port => !usedPorts.Contains(port)).FirstOrDefault();
            return (ushort)unusedPort;
        }
        static void Main(string[] args)
        {
            ushort RELAY_PORT = GetFreePort(6666);
            Console.WriteLine($"RELAY_PORT = {RELAY_PORT}");
            const string RELAY_PASSWORD = "666";
            const string RELAY_ADMIN_PASSWORD = "777";
            Relay relay = new Relay(RELAY_PORT, RELAY_PASSWORD, RELAY_ADMIN_PASSWORD);
            var relayend = new IPEndPoint(IPAddress.Loopback, RELAY_PORT);
            RelayClient client0 = new RelayClient(relayend);
            RelayClient client1 = new RelayClient(relayend);

            var c0c = client0.TryConnect();
            var c1c = client1.TryConnect();

            int i = 0;
            while(true)
            {
                Console.WriteLine($"i");
                Thread.Sleep(100);
                i++;
                if(c0c != null && c0c.IsCompleted)
                {
                    Console.WriteLine($"client0 attempted to connect with result: {c0c.Result}");
                    c0c = null;
                }
                if(c1c != null && c1c.IsCompleted)
                {
                    Console.WriteLine($"client1 attempted to connect with result: {c1c.Result}");
                    c1c = null;
                }
                if(c0c == null && c1c == null)
                    break;
            }
            Console.WriteLine($"Connection stage complete");
            bool b0 = client0.LogInAdmin(RELAY_PASSWORD, RELAY_ADMIN_PASSWORD).Result;
            bool b1 = client1.LogIn(RELAY_PASSWORD).Result;
            if(client0.Status == RelayClient.RCStatus.LOGGED_IN && client0.IsAdmin)
                Console.WriteLine("Client0 successfully logged in as admin!");
            else
            {
                Console.WriteLine("Client0 failed to log in as admin!");
                return;
            }
            if(client1.Status == RelayClient.RCStatus.LOGGED_IN)
                Console.WriteLine("Client1 successfully logged in!");
            else
            {
                Console.WriteLine("Client1 failed to log in!");
                return;
            }
            Console.WriteLine("Login stage complete");

        }
    }
}


