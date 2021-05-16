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
            Apop,
            Quit,
            Stat,
            List,
            Retr,
            Dele,
            Noop,
            Rset,
            Close,
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
                .OnEntryAsync(OnAuthorization)
                .Permit(Pop3SessionTrigger.Apop, Pop3SessionState.Transaction)
                .Permit(Pop3SessionTrigger.Quit, Pop3SessionState.Closed);

            _machine.Configure(Pop3SessionState.Transaction)
                .OnEntryAsync(OnTransaction)
                .PermitReentry(Pop3SessionTrigger.Stat)
                .PermitReentry(Pop3SessionTrigger.List)
                .PermitReentry(Pop3SessionTrigger.Retr)
                .PermitReentry(Pop3SessionTrigger.Dele)
                .PermitReentry(Pop3SessionTrigger.Noop)
                .PermitReentry(Pop3SessionTrigger.Rset)
                .Permit(Pop3SessionTrigger.Quit, Pop3SessionState.Update);

            _machine.Configure(Pop3SessionState.Update)
                .OnEntryAsync(OnUpdate)
                .Permit(Pop3SessionTrigger.Close, Pop3SessionState.Closed);

            _machine.Configure(Pop3SessionState.Closed)
                .OnEntryAsync(OnClosed);

            _machine.OnUnhandledTriggerAsync(async (state, trigger) =>
            {
                await Writer.WriteLineAsync("-ERR");
            });
        }

        private Task OnAuthorization()
        {
            return Writer.WriteLineAsync("+OK POP3 server ready");
        }

        private Task OnTransaction()
        {
            throw new NotImplementedException();
        }

        private Task OnUpdate()
        {
            throw new NotImplementedException();
        }

        private Task OnClosed()
        {
            throw new NotImplementedException();
        }
    }
}