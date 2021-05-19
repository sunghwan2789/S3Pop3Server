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
            Start,
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
            Uidl,
            List,
            Top,
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
        private readonly StateMachine<State, Trigger>.TriggerWithParameters<int?> _uidlTrigger;
        private readonly StateMachine<State, Trigger>.TriggerWithParameters<int?> _listTrigger;
        private readonly StateMachine<State, Trigger>.TriggerWithParameters<int, int> _topTrigger;
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
            _machine = new(State.Start);
            _apopTrigger = _machine.SetTriggerParameters<string, string>(Trigger.Apop);
            _uidlTrigger = _machine.SetTriggerParameters<int?>(Trigger.Uidl);
            _listTrigger = _machine.SetTriggerParameters<int?>(Trigger.List);
            _topTrigger = _machine.SetTriggerParameters<int, int>(Trigger.Top);
            _retrTrigger = _machine.SetTriggerParameters<int>(Trigger.Retr);

            ConfigureStateMachine();
        }

        public async Task Start()
        {
            await _machine.ActivateAsync();
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
                    "UIDL" => Uidl(arguments.Any() ? int.Parse(arguments[0]) : null),
                    "LIST" => List(arguments.Any() ? int.Parse(arguments[0]) : null),
                    "TOP" => Top(int.Parse(arguments[0]), int.Parse(arguments[1])),
                    "RETR" => Retr(int.Parse(arguments[0])),
                    _ => throw new NotImplementedException(command),
                };
                await triggerTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{EndPoint} - Fail", EndPoint);
                await Writer.WriteLineAsync($"-ERR");
            }
        }

        public async Task Apop(string name, string digest)
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

            await _machine.FireAsync(_apopTrigger, name, digest);
        }

        public async Task Quit()
        {
            await _machine.FireAsync(Trigger.Quit);
        }

        public async Task Stat()
        {
            var currentEmails = _emails.Except(_toBeDeleted);
            var count = currentEmails.Count();
            var size = currentEmails.Sum(email => email.Size);
            await Writer.WriteLineAsync($"+OK {count} {size}");

            await _machine.FireAsync(Trigger.Stat);
        }

        public async Task Uidl(int? msg)
        {
            if (msg != null)
            {
                var email = _emails.First(email => email.MessageNumber == msg);
                await Writer.WriteLineAsync($"+OK {email.MessageNumber} {email.Id}");
            }
            else
            {
                var currentEmails = _emails.Except(_toBeDeleted);
                await Writer.WriteLineAsync($"+OK");
                foreach (var email in currentEmails)
                {
                    await Writer.WriteLineAsync($"{email.MessageNumber} {email.Id}");
                }
                await Writer.WriteLineAsync($".");
            }

            await _machine.FireAsync(_uidlTrigger, msg);
        }

        public async Task List(int? msg)
        {
            if (msg != null)
            {
                var email = _emails.First(email => email.MessageNumber == msg);
                await Writer.WriteLineAsync($"+OK {email.MessageNumber} {email.Size}");
            }
            else
            {
                var currentEmails = _emails.Except(_toBeDeleted);
                await Writer.WriteLineAsync($"+OK");
                foreach (var email in currentEmails)
                {
                    await Writer.WriteLineAsync($"{email.MessageNumber} {email.Size}");
                }
                await Writer.WriteLineAsync($".");
            }

            await _machine.FireAsync(_listTrigger, msg);
        }

        public async Task Top(int msg, int n)
        {
            var response = await _mediator.Send(new GetMailboxContentQuery
            {
                Item = _emails.First(email => email.MessageNumber == msg),
            });

            await Writer.WriteLineAsync($"+OK");
            using var reader = new StreamReader(response.ContentStream);

            string line = null;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
            {
                await Writer.WriteLineAsync(line);
            }
            await Writer.WriteLineAsync();

            for (var i = 0; i < n; i++)
            {
                await Writer.WriteLineAsync(await reader.ReadLineAsync());
            }
            await Writer.WriteLineAsync($".");

            await _machine.FireAsync(_topTrigger, msg, n);
        }

        public async Task Retr(int msg)
        {
            var response = await _mediator.Send(new GetMailboxContentQuery
            {
                Item = _emails.First(email => email.MessageNumber == msg),
            });

            await Writer.WriteLineAsync($"+OK");
            await response.ContentStream.CopyToAsync(Writer.BaseStream);
            await Writer.WriteLineAsync();
            await Writer.WriteLineAsync($".");

            await _machine.FireAsync(_retrTrigger, msg);
        }

        private void ConfigureStateMachine()
        {
            _machine.Configure(State.Start)
                .OnActivateAsync(() => _machine.FireAsync(Trigger.Start))
                .Permit(Trigger.Start, State.Authorization);

            _machine.Configure(State.Authorization)
                .OnEntryAsync(OnAuthorization)
                .Permit(Trigger.Apop, State.Transaction)
                .Permit(Trigger.Quit, State.Closed);

            _machine.Configure(State.Transaction)
                .PermitReentry(Trigger.Stat)
                .PermitReentry(Trigger.Uidl)
                .PermitReentry(Trigger.List)
                .PermitReentry(Trigger.Top)
                .PermitReentry(Trigger.Retr)
                .PermitReentry(Trigger.Dele)
                .PermitReentry(Trigger.Noop)
                .PermitReentry(Trigger.Rset)
                .Permit(Trigger.Quit, State.Update);

            _machine.Configure(State.Update)
                .OnEntryAsync(OnUpdate)
                .InitialTransition(State.Closed);

            _machine.Configure(State.Closed)
                .SubstateOf(State.Update)
                .OnEntryAsync(OnClosed);

            _machine.OnTransitioned((t) => _logger.LogDebug("{EndPoint} - Transitioned: {from} - ({trigger}) > {to}", EndPoint, t.Source, t.Trigger, t.Destination));
        }

        private async Task OnAuthorization()
        {
            var timestamp = $"<1896.{DateTime.UtcNow.Ticks}@dbc.mtview.ca.us>";
            await Writer.WriteLineAsync($"+OK POP3 server ready {timestamp}");
        }

        private async Task OnUpdate()
        {
            if (_toBeDeleted.Any())
            {
                throw new NotImplementedException();
            }

            await Writer.WriteLineAsync("+OK");
        }

        private Task OnClosed()
        {
            Client.Close();
            return Task.CompletedTask;
        }
    }
}
