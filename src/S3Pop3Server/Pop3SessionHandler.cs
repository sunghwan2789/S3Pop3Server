using System;
using System.Buffers;
using System.Collections.Immutable;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
using Stateless;

namespace S3Pop3Server
{
    public class Pop3SessionHandler
    {
        private readonly ILogger<Pop3SessionHandler> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public Pop3SessionHandler(ILogger<Pop3SessionHandler> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        public async Task Handle(Pop3Session session, CancellationToken cancellationToken = default)
        {
            await session.Start();

            var buffer = new char[256];
            while
            (
                !cancellationToken.IsCancellationRequested
                && session.Client.Connected
            )
            {
                // Socket.ReceiveTimeout 은 ReadAsync 에서는 효과 없으니까 Read 사용하자
                // https://docs.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.receivetimeout?view=net-5.0#remarks
                var received = await Task.Run(() => session.Reader.Read(buffer), cancellationToken);
                _logger.LogInformation("{EndPoint} - Read {received} bytes", session.EndPoint, received);
                if (received == 0)
                {
                    return;
                }

                var (command, arguments) = GetMessage(buffer[..received]);
                _logger.LogDebug("{EndPoint} - Command {command} / {arguments}", session.EndPoint, command, arguments);
                await session.Invoke(command, arguments);
            }
        }

        private static (string command, string[] arguments) GetMessage(Span<char> buffer)
        {
            var endOfMessage = buffer.IndexOf(ControlChars.CrLf);
            if (endOfMessage != -1)
            {
                buffer = buffer[..endOfMessage];
            }

            var command = buffer;
            var arguments = Span<char>.Empty;

            var startOfArguments = buffer.IndexOf(' ');
            if (startOfArguments != -1)
            {
                command = buffer[..startOfArguments];
                arguments = buffer[(startOfArguments + 1)..];
            }

            return (command.ToString(), arguments.ToString().Split(' '));
        }
    }
}
