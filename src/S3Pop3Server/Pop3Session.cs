using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using S3Pop3Server.Queries;
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

        private readonly IMediator _mediator;
        private readonly ILogger<Pop3Session> _logger;
        private readonly StateMachine<State, Trigger> _machine;
        private readonly StateMachine<State, Trigger>.TriggerWithParameters<string, string> _apopTrigger;
        private readonly StateMachine<State, Trigger>.TriggerWithParameters<int?> _listTrigger;
        private readonly StateMachine<State, Trigger>.TriggerWithParameters<int> _retrTrigger;

        private IImmutableList<Email> _emails;
        private IImmutableSet<Email> _toBeDeleted;

        public Pop3Session(TcpClient client, StreamReader reader, StreamWriter writer, IMediator mediator, ILogger<Pop3Session> logger)
        {
            Client = client;
            Reader = reader;
            Writer = writer;

            _mediator = mediator;
            _logger = logger;
            _machine = new(State.Connected);
            _apopTrigger = _machine.SetTriggerParameters<string, string>(Trigger.Apop);
            _listTrigger = _machine.SetTriggerParameters<int?>(Trigger.List);
            _retrTrigger = _machine.SetTriggerParameters<int>(Trigger.Retr);

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
                    "APOP" => Apop(arguments[0], arguments[1]),
                    "QUIT" => Quit(),
                    "STAT" => Stat(),
                    "LIST" => List(arguments.Any() ? int.Parse(arguments[0]) : null),
                    "RETR" => Retr(int.Parse(arguments[0])),
                    _ => throw new NotImplementedException(command),
                };
                await triggerTask;
            }
            catch (Exception ex)
            {
                await Writer.WriteLineAsync($"-ERR {ex}");
            }
        }

        public Task Apop(string name, string digest)
        {
            return _machine.FireAsync(_apopTrigger, name, digest);
        }

        public Task Quit()
        {
            return _machine.FireAsync(Trigger.Quit);
        }

        public Task Stat()
        {
            return _machine.FireAsync(Trigger.Stat);
        }

        public Task List(int? msg)
        {
            return _machine.FireAsync(_listTrigger, msg);
        }

        public Task Retr(int msg)
        {
            return _machine.FireAsync(_retrTrigger, msg);
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
                .OnEntryFromAsync(Trigger.Stat, OnStat)
                .OnEntryFromAsync(_listTrigger, OnList)
                .PermitReentry(Trigger.Stat)
                .PermitReentry(Trigger.List)
                .InternalTransitionAsync(_retrTrigger, (msg, t) => OnRetr(msg))
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
            var timestamp = $"<1896.{DateTime.UtcNow.Ticks}@dbc.mtview.ca.us>";
            return Writer.WriteLineAsync($"+OK POP3 server ready {timestamp}");
        }

        private async Task OnTransaction(string name, string digest)
        {
            if (name != "admin")
            {
                throw new AuthenticationException();
            }

            var response = await _mediator.Send(new GetMailboxListingQuery());

            _emails = response.Items
                .Select((email, index) => email with
                {
                    MessageNumber = index + 1,
                })
                .ToImmutableList();
            _toBeDeleted = ImmutableHashSet<Email>.Empty;

            await Writer.WriteLineAsync("+OK");
        }

        private async Task OnStat()
        {
            var currentEmails = _emails.Except(_toBeDeleted);
            var count = currentEmails.Count();
            var size = currentEmails.Sum(email => email.Size);
            await Writer.WriteLineAsync($"+OK {count} {size}");
        }

        private async Task OnList(int? msg)
        {
            if (msg != null)
            {
                var email = _emails.First(email => email.MessageNumber == msg);
                await Writer.WriteLineAsync($"+OK {email.MessageNumber} {email.Size}");
                return;
            }

            var currentEmails = _emails.Except(_toBeDeleted);
            await Writer.WriteLineAsync($"+OK");
            foreach (var email in currentEmails)
            {
                await Writer.WriteLineAsync($"{email.MessageNumber} {email.Size}");
            }
            await Writer.WriteLineAsync($".");
        }

        private async Task OnRetr(int msg)
        {
            var response = await _mediator.Send(new GetMailboxContentQuery
            {
                Item = _emails.First(email => email.MessageNumber == msg),
            });

            await Writer.WriteLineAsync($"+OK");
            await response.ContentStream.CopyToAsync(Writer.BaseStream);
            await Writer.WriteLineAsync($".");
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
