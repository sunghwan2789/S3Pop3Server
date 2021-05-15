using System;
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
    public class Pop3ConnectionHandler
    {
        private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(3);

        private readonly ILogger<Pop3ConnectionHandler> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public Pop3ConnectionHandler(ILogger<Pop3ConnectionHandler> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        public async Task Handle(TcpClient client, CancellationToken cancellationToken = default)
        {
            using var _ = client;
            client.ReceiveTimeout = (int)ReceiveTimeout.TotalMilliseconds;

            using var stream = client.GetStream();
            // TODO: SSL 지원하기
            // using var stream = new SslStream(client.GetStream(), false);

            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = ControlChars.CrLf,
            };

            var session = new Pop3Session(client, reader, writer);

            using var scope = _scopeFactory.CreateScope();
            var sessionHandler = scope.ServiceProvider.GetRequiredService<Pop3SessionHandler>();
            try
            {
                await sessionHandler.Handle(session, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{@EndPoint} - Error handling a session", session.EndPoint);
            }
        }
    }
}
