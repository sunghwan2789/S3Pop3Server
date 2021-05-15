using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace S3Pop3Server
{
    public class Pop3Listener : BackgroundService
    {
        private static readonly int Pop3ListenerPort = 110;

        private readonly ILogger<Pop3Listener> _logger;
        private readonly Pop3ConnectionHandler _connectionHandler;
        private readonly TcpListener _listener;

        public Pop3Listener(ILogger<Pop3Listener> logger, Pop3ConnectionHandler connectionHandler)
        {
            _logger = logger;
            _connectionHandler = connectionHandler;
            _listener = TcpListener.Create(Pop3ListenerPort);
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _listener.Start();
            return base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Start AcceptTcpClient");
                    var client = await _listener.AcceptTcpClientAsync();
                    _logger.LogInformation("End AcceptTcpClient: {@EndPoint}", client.Client.RemoteEndPoint);

                    _ = _connectionHandler.Handle(client, stoppingToken);
                }
                catch (SocketException ex)
                {
                    _logger.LogError(ex, "AcceptTcpClient");
                }
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _listener.Stop();
            return base.StopAsync(cancellationToken);
        }
    }
}
