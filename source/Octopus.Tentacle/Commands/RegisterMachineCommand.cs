﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Octopus.Client.Model;
using Octopus.Client.Operations;
using Octopus.Shared.Configuration;
using Octopus.Shared.Startup;
using Octopus.Shared.Util;
using Octopus.Tentacle.Commands.OptionSets;
using Octopus.Tentacle.Communications;
using Octopus.Client;
using Octopus.Diagnostics;
using Octopus.Shared;
using Octopus.Tentacle.Configuration;
using Octopus.Tentacle.Properties;
using Halibut;

namespace Octopus.Tentacle.Commands
{
    public class RegisterMachineCommand : AbstractStandardCommand
    {
        readonly Lazy<IRegisterMachineOperation> lazyRegisterMachineOperation;
        readonly Lazy<ITentacleConfiguration> configuration;
        readonly Lazy<IOctopusServerChecker> octopusServerChecker;
        readonly IProxyConfigParser proxyConfig;
        readonly List<string> environmentNames = new List<string>();
        readonly List<string> roles = new List<string>();
        readonly List<string> tenants = new List<string>();
        readonly List<string> tenantTgs = new List<string>();
        readonly ILog log;
        readonly ApiEndpointOptions api;
        string name;
        string policy;
        string publicName;
        bool allowOverwrite;
        string comms = "TentaclePassive";
        int serverCommsPort = 10943;
        string serverWebSocketAddress;
        int? tentacleCommsPort = null;

        public RegisterMachineCommand(Lazy<IRegisterMachineOperation> lazyRegisterMachineOperation, Lazy<ITentacleConfiguration> configuration, ILog log, IApplicationInstanceSelector selector, Lazy<IOctopusServerChecker> octopusServerChecker, IProxyConfigParser proxyConfig)
            : base(selector)
        {
            this.lazyRegisterMachineOperation = lazyRegisterMachineOperation;
            this.configuration = configuration;
            this.log = log;
            this.octopusServerChecker = octopusServerChecker;
            this.proxyConfig = proxyConfig;

            api = AddOptionSet(new ApiEndpointOptions(Options));

            Options.Add("env|environment=", "The environment name to add the machine to - e.g., 'Production'; specify this argument multiple times to add multiple environments", s => environmentNames.Add(s));
            Options.Add("r|role=", "The machine role that the machine will assume - e.g., 'web-server'; specify this argument multiple times to add multiple roles", s => roles.Add(s));
            Options.Add("tenant=", "A tenant who the machine will be connected to; specify this argument multiple times to add multiple tenants", s => tenants.Add(s));
            Options.Add("tenanttag=", "A tenant tag which the machine will be tagged with - e.g., 'CustomerType/VIP'; specify this argument multiple times to add multiple tenant tags", s => tenantTgs.Add(s));
            Options.Add("name=", "Name of the machine when registered; the default is the hostname", s => name = s);
            Options.Add("policy=", "The name of a machine policy that applies to this machine", s => policy = s);
            Options.Add("h|publicHostName=", "An Octopus-accessible DNS name/IP address for this machine; the default is the hostname", s => publicName = s);
            Options.Add("f|force", "Allow overwriting of existing machines", s => allowOverwrite = true);
            Options.Add("comms-style=", "The communication style to use - either TentacleActive or TentaclePassive; the default is " + comms, s => comms = s);
            Options.Add("server-comms-port=", "When using active communication, the comms port on the Octopus server; the default is " + serverCommsPort, s => serverCommsPort = int.Parse(s));
            Options.Add("server-web-socket=", "When using active communication over websockets, the address of the Octopus server, eg 'wss://example.com/OctopusComms'. Refer to http://g.octopushq.com/WebSocketComms", s => serverWebSocketAddress = s);
            Options.Add("tentacle-comms-port=", "When using passive communication, the comms port that the Octopus server is instructed to call back on to reach this machine; defaults to the configured listening port", s => tentacleCommsPort = int.Parse(s));
        }

        protected override void Start()
        {
            base.Start();
            StartAsync().GetAwaiter().GetResult();
        }

        async Task StartAsync()
        {
            if (environmentNames.Count == 0 || string.IsNullOrWhiteSpace(environmentNames.First()))
                throw new ArgumentException("Please specify an environment name, e.g., --environment=Development");

            CommunicationStyle communicationStyle;
            if (!Enum.TryParse(comms, true, out communicationStyle))
                throw new ArgumentException("Please specify a valid communications style, e.g. --comms-style=TentaclePassive");

            if (configuration.Value.TentacleCertificate == null)
                throw new ArgumentException("No certificate has been generated for this Tentacle. Please run the new-certificate command first.");

            Uri serverAddress = null;

            //if we are on a polling tentacle with a polling proxy set up, use the api through that proxy
            IWebProxy proxyOverride = null;
            string sslThumbprint = null;
            if (communicationStyle == CommunicationStyle.TentacleActive)
            {
                serverAddress = GetActiveTentacleAddress();
                proxyOverride = proxyConfig.ParseToWebProxy(configuration.Value.PollingProxyConfiguration);
                sslThumbprint = octopusServerChecker.Value.CheckServerCommunicationsIsOpen(serverAddress, proxyOverride);
            }

            log.Info($"Registering the tentacle with the server at {api.ServerUri}");

            using (var client = await api.CreateClient(proxyOverride))
            {
                var repository = new OctopusAsyncRepository(client);

                ConfirmTentacleCanRegisterWithServerBasedOnItsVersion(repository);

                var machine = new OctopusServerConfiguration(await GetServerThumbprint(repository, serverAddress, sslThumbprint))
                {
                    Address = serverAddress,
                    CommunicationStyle = communicationStyle
                };

                var registerMachineOperation = lazyRegisterMachineOperation.Value;
                registerMachineOperation.MachineName = string.IsNullOrWhiteSpace(name) ? Environment.MachineName : name;

                if (communicationStyle == CommunicationStyle.TentaclePassive)
                {
                    registerMachineOperation.TentacleHostname = string.IsNullOrWhiteSpace(publicName) ? Environment.MachineName : publicName;
                    registerMachineOperation.TentaclePort = tentacleCommsPort ?? configuration.Value.ServicesPortNumber;
                }
                else if (communicationStyle == CommunicationStyle.TentacleActive)
                {
                    var subscriptionId = new Uri("poll://" + RandomStringGenerator.Generate(20).ToLowerInvariant() + "/");
                    registerMachineOperation.SubscriptionId = subscriptionId;
                    machine.SubscriptionId = subscriptionId.ToString();
                }

                registerMachineOperation.Tenants = tenants.ToArray();
                registerMachineOperation.TenantTags = tenantTgs.ToArray();
                registerMachineOperation.TentacleThumbprint = configuration.Value.TentacleCertificate.Thumbprint;
                registerMachineOperation.EnvironmentNames = environmentNames.ToArray();
                registerMachineOperation.Roles = roles.ToArray();
                registerMachineOperation.MachinePolicy = policy;
                registerMachineOperation.AllowOverwrite = allowOverwrite;
                registerMachineOperation.CommunicationStyle = communicationStyle;
                await registerMachineOperation.ExecuteAsync(repository);


                configuration.Value.AddOrUpdateTrustedOctopusServer(machine); 

                log.Info("Machine registered successfully");
            }
        }

        async Task<string> GetServerThumbprint(IOctopusAsyncRepository repository, Uri serverAddress, string sslThumbprint)
        {
            if (serverAddress != null && ServiceEndPoint.IsWebSocketAddress(serverAddress))
            {
                if (sslThumbprint == null)
                    throw new Exception($"Could not determine thumbprint of the SSL Certificate at {serverAddress}");
                return sslThumbprint;
            }
            return (await repository.CertificateConfiguration.GetOctopusCertificate()).Thumbprint;
        }

        Uri GetActiveTentacleAddress()
        {
            if (string.IsNullOrWhiteSpace(serverWebSocketAddress))
                return new Uri($"https://{api.ServerUri.Host}:{serverCommsPort}");

            if (!HalibutRuntime.OSSupportsWebSockets)
                throw new Exception("Websockets is only supported on Windows Server 2012 and later");

            var address = new Uri(serverWebSocketAddress);

            switch (address.Scheme.ToLower())
            {
                case "https":
                    address = new UriBuilder(address) { Scheme = "wss" }.Uri;
                    break;
                case "wss":
                    break;
                default:
                    throw new ArgumentException("The websocket address must start with wss://");
            }

            return address;
        }

        #region Helpers

        void ConfirmTentacleCanRegisterWithServerBasedOnItsVersion(OctopusAsyncRepository repository)
        {
            // Eg. Check they're not trying to register a 3.* Tentacle with a 2.* API Server.
            if (string.IsNullOrEmpty(repository.Client.RootDocument.Version))
                throw new ControlledFailureException("Unable to determine the Octopus Server version.");

            var serverVersion = SemanticVersion.Parse(repository.Client.RootDocument.Version);
            var tentacleVersion = SemanticVersion.Parse(OctopusTentacle.SemanticVersionInfo.MajorMinorPatch);
            if (serverVersion.Version.Major < tentacleVersion.Version.Major)
                throw new ControlledFailureException($"You cannot register a {tentacleVersion.Version.Major}.* Octopus Tentacle with a {serverVersion.Version.Major}.* Octopus Server.");
        }

        #endregion

    }
}