using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Stateless;

namespace S3Pop3Server
{
    public class Pop3Session
    {
        private enum Pop3SessionState
        {
            Connected,
            Authorization,
            Transaction,
            Update,
            Closed,
        }

        private enum Pop3SessionTrigger
        {
            Start,
            User,
            Pass,
            Apop,
        }

        public TcpClient Client { get; }
        public EndPoint EndPoint => Client.Client.RemoteEndPoint;
        public StreamReader Reader { get; }
        public StreamWriter Writer { get; }
        private readonly StateMachine<Pop3SessionState, Pop3SessionTrigger> _machine;

        public Pop3Session(TcpClient client, StreamReader reader, StreamWriter writer)
        {
            Client = client;
            Reader = reader;
            Writer = writer;
            _machine = new StateMachine<Pop3SessionState, Pop3SessionTrigger>(Pop3SessionState.Connected);

            ConfigureStateMachine();
        }

        public Task Start()
        {
            return _machine.FireAsync(Pop3SessionTrigger.Start);
        }

        private void ConfigureStateMachine()
        {
            _machine.Configure(Pop3SessionState.Connected)
                .Permit(Pop3SessionTrigger.Start, Pop3SessionState.Authorization);

            _machine.Configure(Pop3SessionState.Authorization)
                .OnEntryAsync(OnAuthorization);

            _machine.OnUnhandledTriggerAsync(async (state, trigger) =>
            {
            });
        }

        private Task OnAuthorization()
        {
            return Writer.WriteLineAsync("+OK POP3 server ready");
        }
    }
}