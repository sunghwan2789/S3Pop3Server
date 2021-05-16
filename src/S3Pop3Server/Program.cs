using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Channels;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace S3Pop3Server
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<Pop3Listener>();
                    services.AddTransient<Pop3ConnectionHandler>();
                    services.AddTransient<Pop3SessionHandler>();

                    var awsOptions = hostContext.Configuration.GetAWSOptions();
                    awsOptions.Credentials = new BasicAWSCredentials(
                        hostContext.Configuration["AWS:AccessKey"],
                        hostContext.Configuration["AWS:SecretKey"]);
                    // https://stackoverflow.com/a/48312720
                    // awsOptions.Credentials = new EnvironmentVariablesAWSCredentials();
                    services.AddDefaultAWSOptions(awsOptions);
                    services.AddAWSService<IAmazonS3>();
                });
    }
}
