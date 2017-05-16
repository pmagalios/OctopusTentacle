using System.Net;
using Autofac;
using Octopus.Shared.Configuration;
using Octopus.Shared.Diagnostics;
using Octopus.Shared.Security;
using Octopus.Shared.Startup;
using Octopus.Shared.Time;
using Octopus.Shared.Util;
using Octopus.Shared.Versioning;
using Octopus.Tentacle.Commands;
using Octopus.Tentacle.Communications;
using Octopus.Tentacle.Configuration;
using Octopus.Tentacle.Properties;
using Octopus.Tentacle.Services;

namespace Octopus.Tentacle
{
    public class Program : OctopusProgram
    {
        public Program(string[] commandLineArguments) : base("Octopus Deploy: Tentacle",
            OctopusTentacle.Version.ToString(),
            OctopusTentacle.InformationalVersion,
            OctopusTentacle.EnvironmentInformation,
            commandLineArguments)
        {
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Ssl3
                | SecurityProtocolType.Tls
                | SecurityProtocolType.Tls11
                | SecurityProtocolType.Tls12;
        }

        static int Main(string[] args)
        {
            return new Program(args).Run();
        }

        protected override IContainer BuildContainer(string instanceName)
        {
            var builder = new ContainerBuilder();
            const ApplicationName applicationName = ApplicationName.Tentacle;

            builder.RegisterModule(new ConfigurationModule(applicationName, instanceName));
            builder.RegisterModule(new TentacleConfigurationModule());
            builder.RegisterModule(new LoggingModule());
            builder.RegisterModule(new OctopusFileSystemModule());
            builder.RegisterModule(new CertificatesModule());
            builder.RegisterModule(new TimeModule());
            builder.RegisterModule(new ClientModule());
            builder.RegisterModule(new TentacleCommunicationsModule(applicationName));
            builder.RegisterModule(new ServicesModule());
            builder.RegisterModule(new VersioningModule(GetType().Assembly));

            builder.RegisterCommand<CreateInstanceCommand>("create-instance", "Registers a new instance of the Tentacle service");
            builder.RegisterCommand<DeleteInstanceCommand>("delete-instance", "Deletes an instance of the Tentacle service");
            builder.RegisterCommand<WatchdogCommand>("watchdog", "Configure a scheduled task to monitor the Tentacle service(s)")
                .WithParameter("applicationName", ApplicationName.Tentacle);
            builder.RegisterCommand<CheckServicesCommand>("checkservices", "Checks the Tentacle instances are running")
                .WithParameter("applicationName", ApplicationName.Tentacle);
            builder.RegisterCommand<RunAgentCommand>("agent", "Starts the Tentacle Agent in debug mode", "", "run");
            builder.RegisterCommand<ConfigureCommand>("configure", "Sets Tentacle settings such as the port number and thumbprints");
            builder.RegisterCommand<RegisterMachineCommand>("register-with", "Registers this machine with an Octopus Server");
            builder.RegisterCommand<ExtractCommand>("extract", "Extracts a NuGet package");
            builder.RegisterCommand<DeregisterMachineCommand>("deregister-from", "Deregisters this machine from an Octopus Server");
            builder.RegisterCommand<NewCertificateCommand>("new-certificate", "Creates and installs a new certificate for this Tentacle");
            builder.RegisterCommand<ShowThumbprintCommand>("show-thumbprint", "Show the thumbprint of this Tentacle's certificate");
            builder.RegisterCommand<ServiceCommand>("service", "Start, stop, install and configure the Tentacle service").WithParameter("serviceName", "OctopusDeploy Tentacle").WithParameter("serviceDescription", "Octopus Deploy: Tentacle deployment agent").WithParameter("assemblyContainingService", typeof (Program).Assembly);
            builder.RegisterCommand<ProxyConfigurationCommand>("proxy", "Configure the HTTP proxy used by Octopus");
            builder.RegisterCommand<PollingProxyConfigurationCommand>("polling-proxy", "Configure the HTTP proxy used by polling tentacles to reach the Octopus Server");
            builder.RegisterCommand<ServerCommsCommand>("server-comms", "Configure how the Tentacle communicates with an Octopus Server");
            builder.RegisterCommand<ImportCertificateCommand>("import-certificate", "Replace the certificate that Tentacle uses to authenticate itself");
            builder.RegisterCommand<PollCommand>("poll-server", "Configures an Octopus Server that this Tentacle will poll");
            builder.RegisterCommand<ListInstancesCommand>("list-instances", "Lists all installed Tentacle instances");

            return builder.Build();
        }
    }
}