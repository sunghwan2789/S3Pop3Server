using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Channels;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using S3Pop3Server;
using Serilog;

var builder = Host.CreateDefaultBuilder(args);
builder.ConfigureServices((hostContext, services) =>
{
    services.AddHostedService<Pop3Listener>();
    services.AddTransient<Pop3ConnectionHandler>();
    services.AddTransient<Pop3SessionHandler>();

    services.AddMediatR(Assembly.GetExecutingAssembly());

    var awsOptions = hostContext.Configuration.GetAWSOptions();
    awsOptions.Credentials = new BasicAWSCredentials(
        hostContext.Configuration["AWS:AccessKey"],
        hostContext.Configuration["AWS:SecretKey"]);
    // https://stackoverflow.com/a/48312720
    try
    {
        awsOptions.Credentials = new EnvironmentVariablesAWSCredentials();
    }
    catch (InvalidOperationException)
    {
        // noop, use appsettings
    }
    services.AddDefaultAWSOptions(awsOptions);
    services.AddAWSService<IAmazonS3>();
});
builder.UseSerilog((hostContext, services, logger) =>
{
    logger.MinimumLevel.Verbose();
    logger.Enrich.FromLogContext();
    logger.WriteTo.Console();
});

using var app = builder.Build();

await app.RunAsync();
