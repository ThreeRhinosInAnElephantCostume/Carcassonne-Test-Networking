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

namespace Carcassonne_Test_Networking
{
    class Program
    {
        const string admin_password = "666";
        const string client_password = "777";
        public static int Hash(int v)
        {
            return (int)((v * 555) ^ 0xFA0196F4);
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct MHostAccepted
        {
            public long clientid;
            public long gameid;
            public int playindex;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct MLock
        {
            public int nplayers;
            public int initial;
            public int hash;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        public struct MMakeMove
        {
            public int moveindx;
            public int dif;
            public int hash;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct MConfirmState
        {
            public int moveindx;
            public int hash;
        }
        public enum Messages
        {
            HOST_ACCEPTED,
            LOCK,
            HOST_READY,
            START,
            MAKE_MOVE,
            CONFIRM_STATE,
        }
        public class RelayServerTestHandles : IRelayServerHandlers
        {
            public void FailHandle(NetException ex)
            {
                throw new NotImplementedException();
            }
        }
        public abstract class Machine : IRelayClientHandlers
        {
            public RelayClient Client;
            public List<System.Action> actions = new List<Action>();
            public Dictionary<long, long> ConIDByPlayID = new Dictionary<long, long>();
            public Dictionary<long, long> PlayIDByConID = new Dictionary<long, long>();

            public void AddCon(long conid, long playid)
            {
                ConIDByPlayID.Add(conid, playid);
                PlayIDByConID.Add(playid, conid);
            }

            public abstract bool WantsAdmin();

            public void FailHandle(NetException ex)
            {
                throw new NotImplementedException();
            }

            public void OnPeerLost(Peer.Connection con, DisconnectReason reason, DisconnectType type)
            {
                throw new NotImplementedException();
            }

            public void OnLoginFailure()
            {
                throw new NotImplementedException();
            }

            public void OnLoginSuccess(Peer.Connection con)
            {
                lock(actions) actions.Add(()=>OnLogin(con));
            }

            public void OnPeerDiscovered(Peer.Connection con, bool isadmin)
            {
                lock(actions) actions.Add(()=>Discovered(con, isadmin));
            }

            public void OnReceived(Message msg)
            {
                lock(actions) actions.Add(()=>DoStuff(msg));
            }

            public void OnStatus(MStatus stat)
            {
                throw new NotImplementedException();
            }

            public void OnDisconnected(DisconnectReason reason, DisconnectType type)
            {
                throw new NotImplementedException();
                Console.WriteLine($"Disconnected {reason}");
                lock(actions)
                {
                    actions.Add(() => Connect());
                }
            }
            public abstract void OnLogin(Peer.Connection relay);
            public abstract void Discovered(Peer.Connection con, bool isadmin);
            public abstract void DoStuff(Message msg);
            void Connect()
            {
                Client.ConnectToServer(Client.RelayEndpoint);
            }
            public void Start()
            {
                Client.Start();
            }
            public virtual void Tick()
            {
                var l = new List<System.Action>();
                lock(actions)
                {
                    l = actions.ToList();
                    actions.Clear();
                }
                    
                foreach(var it in l)
                {
                    it();
                }
            }
            public Machine(IPEndPoint end, ushort myport, string password)
            {
                Client = new RelayClient(this, end, myport, password, WantsAdmin(), false);
            }
        }
        public class GameServer : Machine
        {
            long concount = 0;
            public List<(long conid, long gameid)> discovered = new List<(long conid, long gameid)>();
            public List<(long conid, long gameid)> accepted = new List<(long conid, long gameid)>();
            public List<(long conid, long gameid)> ready = new List<(long conid, long gameid)>();


            public override void Discovered(Peer.Connection con, bool isadmin)
            {
                discovered.Add(new (con.ID, concount++));
            }

            public override void DoStuff(Message msg)
            {
                switch((Messages)msg.MType)
                {
                    case Messages.HOST_READY:
                    {
                        var a = accepted.Find(it => it.conid == msg.SenderID);
                        ready.Add(a);
                        AddCon(a.conid, a.gameid);
                        if(ready.Count == accepted.Count)
                        {
                            Client.SendMessage(ID_ALL, (int)Messages.START, new byte[1], (mid) => 
                            {
                                throw new NotImplementedException();
                            }, (mid) => {});
                        }
                        break;
                    }
                }
            }

            public override void OnLogin(Peer.Connection relay)
            {
                Console.WriteLine("Server logged in");
            }

            public override bool WantsAdmin()
            {
                return true;
            }
            public void StartGame(int initialval)
            {
                lock(discovered) accepted = discovered.ToList();
                foreach(var it in accepted)
                {
                    Client.SendMessage(ID_ALL, (int)Messages.HOST_ACCEPTED, SerializeStruct(new MHostAccepted()
                    {
                        clientid = it.conid,
                        gameid = it.gameid,
                        playindex = accepted.IndexOf(it),
                    }), (mid) => 
                    {
                        throw new NotImplementedException();
                    }, (mid) => {});
                }

            }
            public GameServer(IPEndPoint end, ushort myport, string password) : base(end, myport, password)
            {

            }
        }
        public class GameClient : Machine
        {
            long myid;
            int state;
            int totplayers = 0;
            int knownplayers = 0;
            int _curplayerindx = 0;
            long nextplayer => Players[(_curplayerindx+1)%Players.Length];  
            long curplayer => Players[_curplayerindx];
            long curplayercon => ConIDByPlayID[curplayer];
            long[] Players = null;
            bool started = false;
            int curmove = 0;
            Dictionary<int, int> movereadiness = new Dictionary<int, int>();
            void NoteReady(int move)
            {
                if(!movereadiness.ContainsKey(move))
                    movereadiness.Add(move, 0);
                movereadiness[move]++;
            }
            int NReady(int move)
            {
                if(curmove == 0)
                    return totplayers;
                if(!movereadiness.ContainsKey(move))
                    return 0;
                return movereadiness[move];
            }
            public override void Discovered(Peer.Connection con, bool isadmin)
            {
                
            }
            public void Play()
            {
                Assert(curplayer == myid);
                int dif = (int)(new RNG((ulong)state).NextLong() % 20)-10;
                state += dif;
                Client.SendMessage(ID_ALL, (int)Messages.HOST_ACCEPTED, SerializeStruct(new MMakeMove()
                {
                    dif=dif,
                    hash=Hash(state),
                    moveindx=curmove,
                }), (mid) => 
                {
                    throw new NotImplementedException();
                }, (mid) => {});

                Console.WriteLine($"New state from {myid}: {state}");
                _curplayerindx = (_curplayerindx+1) % Players.Length;
                curmove++;
            }
            public override void DoStuff(Message msg)
            {
                switch((Messages)msg.MType)
                {
                    case Messages.LOCK:
                    {
                        var m = DeserializeStruct<MLock>(msg.Data);
                        state = m.initial;
                        Assert(Hash(state) == m.hash);
                        totplayers = m.nplayers;
                        Players = new long[totplayers];
                        break;
                    }
                    case Messages.HOST_ACCEPTED:
                    {
                        var m = DeserializeStruct<MHostAccepted>(msg.Data);
                        if(m.clientid == Client.ID)
                            myid = m.gameid;
                        AddCon(m.clientid, m.gameid);
                        knownplayers++;
                        Players[m.playindex] = m.gameid;
                        if(knownplayers == totplayers)
                        {
                            Client.SendMessage(ID_ALL, (int)Messages.HOST_READY, null, (mid) => 
                            {
                                throw new NotImplementedException();
                            }, (mid) => {});
                        }
                        break;
                    }
                    case Messages.START:
                    {
                        started = true;
                        break;
                    }
                    case Messages.MAKE_MOVE:
                    {
                        var m = DeserializeStruct<MMakeMove>(msg.Data);
                        int nstate = state + m.dif;
                        if(m.moveindx < curmove)
                        {
                            if(m.moveindx == curmove+1)
                            {
                                Assert(m.hash == Hash(state));
                            }
                            else
                            {
                                lock(actions) actions.Add(() => DoStuff(msg));
                            }
                            break;
                        }
                        if(m.moveindx > curmove)
                        {
                            lock(actions) actions.Add(() => DoStuff(msg));
                            break;
                        }
                        Assert(Hash(nstate) == m.hash);
                        state = nstate;
                        Client.SendMessage(ID_ALL, (int)Messages.HOST_ACCEPTED, SerializeStruct(new MConfirmState()
                        {
                            moveindx=m.moveindx,
                            hash=Hash(state),
                        }), (mid) => 
                        {
                            throw new NotImplementedException();
                        }, (mid) => {});
                        curmove = m.moveindx;
                        _curplayerindx = ((int)PlayIDByConID[msg.SenderID]+1) % Players.Length;
                        break;
                    }
                    case Messages.CONFIRM_STATE:
                    {
                        var m = DeserializeStruct<MConfirmState>(msg.Data);
                        Assert(m.moveindx != curmove || m.hash == Hash(state));
                        NoteReady(m.moveindx);
                        break;
                    }
                }
            }
            public override void Tick()
            {
                base.Tick();
                if(started)
                {
                    if(myid == curplayer && NReady(curmove) == totplayers)
                    {
                        Play();
                    }
                }
            }
            public override void OnLogin(Peer.Connection relay)
            {
                Console.WriteLine($"Client logged in, got id: {Client.ID}");
            }

            public override bool WantsAdmin()
            {
                return false;
            }
            public GameClient(IPEndPoint end, ushort myport, string password) : base(end, myport, password)
            {

            }
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct foo
        {
            public int v0;
            private VArray128 _data;
            public byte[] Data { get => _data.Data; set => _data.Data = value; }
        }

        static void Main(string[] args)
        {
            const int N_PLAY = 1;
            const ushort relport = 60000;
            const ushort conport = 60010;

            RelayServer relay;

            GameServer admin;

            List<GameClient> clients = new List<GameClient>();

            var relend = new IPEndPoint(IPAddress.Loopback, relport);

            
            relay = new RelayServer(new RelayServerTestHandles(), (ushort)relend.Port, admin_password, client_password, false);

            relay.Start();

            admin = new GameServer(relend, conport, admin_password);
            admin.Start();
            admin.Tick();

            for(int i = 0; i < N_PLAY; i++)
            {
                var c = new GameClient(relend, (ushort)(conport+1+i), client_password);
                c.Start();
                clients.Add(c);
            }
            
            foreach(var it in clients)
            {
                while(!it.Client.IsLoggedIn)
                {
                    Console.WriteLine("3242");
                    Thread.Sleep(1000);
                }
                it.Tick();
                Thread.Sleep(1000);
            }
            Thread.Sleep(1000);
            Console.WriteLine(admin.accepted);
            foreach(var it in clients)
            {
                it.Tick();
            }
            admin.Tick();

            bool started = false;
            while(true)
            {
                if(!started && admin.PlayIDByConID.Count == N_PLAY)
                {
                    admin.StartGame(666);
                    started = true;
                }
                admin.Tick();
                foreach(var it in clients)
                {
                    it.Tick();
                }
                Thread.Sleep(1);
            }
        }
    }
}
