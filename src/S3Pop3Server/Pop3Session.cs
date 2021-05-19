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
                    "DELE" => Dele(int.Parse(arguments[0])),
                    "NOOP" => Noop(),
                    "RSET" => Rset(),
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
            _machine.EnsurePermitted(Trigger.Apop);

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

            await _machine.FireAsync(Trigger.Apop);
        }

        public async Task Quit()
        {
            await _machine.FireAsync(Trigger.Quit);
        }

        public async Task Stat()
        {
            _machine.EnsurePermitted(Trigger.Stat);

            var currentEmails = _emails.Except(_toBeDeleted);
            var count = currentEmails.Count();
            var size = currentEmails.Sum(email => email.Size);
            await Writer.WriteLineAsync($"+OK {count} {size}");

            await _machine.FireAsync(Trigger.Stat);
        }

        public async Task Uidl(int? msg)
        {
            _machine.EnsurePermitted(Trigger.Uidl);

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

            await _machine.FireAsync(Trigger.Uidl);
        }

        public async Task List(int? msg)
        {
            _machine.EnsurePermitted(Trigger.List);

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

            await _machine.FireAsync(Trigger.List);
        }

        public async Task Top(int msg, int n)
        {
            _machine.EnsurePermitted(Trigger.Top);

            var response = await _mediator.Send(new GetMailboxContentQuery
            {
                Item = _emails.First(email => email.MessageNumber == msg),
            });

            await Writer.WriteLineAsync($"+OK");
            using var reader = new StreamReader(response.ContentStream);

            string line = null;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
            {
                if (ShouldByteStuffed(line))
                {
                    await Writer.WriteAsync('.');
                }
                await Writer.WriteLineAsync(line);
            }
            await Writer.WriteLineAsync();

            for (var i = 0; i < n && !reader.EndOfStream; i++)
            {
                line = await reader.ReadLineAsync();
                if (ShouldByteStuffed(line))
                {
                    await Writer.WriteAsync('.');
                }
                await Writer.WriteLineAsync(line);
            }
            await Writer.WriteLineAsync($".");

            await _machine.FireAsync(Trigger.Top);
        }

        public async Task Retr(int msg)
        {
            _machine.EnsurePermitted(Trigger.Retr);

            var response = await _mediator.Send(new GetMailboxContentQuery
            {
                Item = _emails.First(email => email.MessageNumber == msg),
            });

            await Writer.WriteLineAsync($"+OK");
            using var reader = new StreamReader(response.ContentStream);
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (ShouldByteStuffed(line))
                {
                    await Writer.WriteAsync('.');
                }
                await Writer.WriteLineAsync(line);
            }
            await Writer.WriteLineAsync($".");

            await _machine.FireAsync(Trigger.Retr);
        }

        public async Task Dele(int msg)
        {
            _machine.EnsurePermitted(Trigger.Dele);

            var email = _emails.First(email => email.MessageNumber == msg);
            _toBeDeleted = _toBeDeleted.Add(email);
            await Writer.WriteLineAsync($"+OK");

            await _machine.FireAsync(Trigger.Dele);
        }

        public async Task Noop()
        {
            _machine.EnsurePermitted(Trigger.Noop);

            await Writer.WriteLineAsync($"+OK");

            await _machine.FireAsync(Trigger.Noop);
        }

        public async Task Rset()
        {
            _machine.EnsurePermitted(Trigger.Rset);

            _toBeDeleted = _toBeDeleted.Clear();

            await _machine.FireAsync(Trigger.Rset);
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
                .Permit(Trigger.Close, State.Closed);

            _machine.Configure(State.Closed)
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

            await _machine.FireAsync(Trigger.Close);
        }

        private async Task OnClosed()
        {
            await Writer.WriteLineAsync("+OK");
            Client.Close();
        }

        private static bool ShouldByteStuffed(ReadOnlySpan<char> line)
        {
            return line.StartsWith(".");
        }
    }
}
