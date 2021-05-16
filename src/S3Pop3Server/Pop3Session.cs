using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Stateless;

namespace S3Pop3Server
{
    public class Pop3Session
    {
        private enum State
        {
            Connected,
            Authorization,
            Transaction,
            Update,
            Closed,
        }

        private enum Trigger
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
        public NetworkCredential Credential { get; private set; }

        private readonly ILogger<Pop3Session> _logger;
        private readonly StateMachine<State, Trigger> _machine;
        private readonly StateMachine<State, Trigger>.TriggerWithParameters<NetworkCredential> _apopTrigger;

        public Pop3Session(TcpClient client, StreamReader reader, StreamWriter writer, ILogger<Pop3Session> logger)
        {
            Client = client;
            Reader = reader;
            Writer = writer;

            _logger = logger;
            _machine = new(State.Connected);
            _apopTrigger = _machine.SetTriggerParameters<NetworkCredential>(Trigger.Apop);

            ConfigureStateMachine();
        }

        public Task Start()
        {
            return _machine.FireAsync(Trigger.Start);
        }

        public async Task Invoke(string command, string[] arguments)
        {
            try
            {
                var triggerTask = command.ToUpperInvariant() switch
                {
                    "APOP" => Apop(new(arguments[0], arguments[1])),
                    "QUIT" => Quit(),
                    _ => throw new NotImplementedException(command),
                };
                await triggerTask;
            }
            catch (Exception ex)
            {
                await Writer.WriteLineAsync($"-ERR {ex}");
            }
        }

        public Task Apop(NetworkCredential credential)
        {
            return _machine.FireAsync(_apopTrigger, credential);
        }

        public Task Quit()
        {
            return _machine.FireAsync(Trigger.Quit);
        }

        private void ConfigureStateMachine()
        {
            _machine.Configure(State.Connected)
                .Permit(Trigger.Start, State.Authorization);

            _machine.Configure(State.Authorization)
                .OnEntryAsync(OnAuthorization)
                .Permit(Trigger.Apop, State.Transaction)
                .Permit(Trigger.Quit, State.Closed);

            _machine.Configure(State.Transaction)
                .OnEntryFromAsync(_apopTrigger, OnTransaction)
                .PermitReentry(Trigger.Stat)
                .PermitReentry(Trigger.List)
                .PermitReentry(Trigger.Retr)
                .PermitReentry(Trigger.Dele)
                .PermitReentry(Trigger.Noop)
                .PermitReentry(Trigger.Rset)
                .Permit(Trigger.Quit, State.Update);

            _machine.Configure(State.Update)
                .OnEntryAsync(OnUpdate)
                .Permit(Trigger.Close, State.Closed);

            _machine.Configure(State.Closed)
                .OnEntryAsync(OnClosed);
        }

        private Task OnAuthorization()
        {
            return Writer.WriteLineAsync("+OK POP3 server ready");
        }

        private Task OnTransaction(NetworkCredential credential)
        {
            if (credential.UserName != "admin")
            {
                throw new AuthenticationException();
            }

            Credential = credential;

            return Writer.WriteLineAsync("+OK");
        }

        private Task OnUpdate()
        {
            throw new NotImplementedException();
        }

        private Task OnClosed()
        {
            Client.Close();
            return Task.CompletedTask;
        }
    }
}
