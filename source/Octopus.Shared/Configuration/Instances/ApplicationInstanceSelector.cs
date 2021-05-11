using System;
using System.Linq;
using Octopus.CoreUtilities.Extensions;
using Octopus.Shared.Util;

namespace Octopus.Shared.Configuration.Instances
{
    class ApplicationInstanceSelector : IApplicationInstanceSelector
    {
        readonly IApplicationInstanceStore applicationInstanceStore;
        readonly StartUpInstanceRequest startUpInstanceRequest;
        readonly IApplicationConfigurationContributor[] instanceStrategies;
        readonly IOctopusFileSystem fileSystem;
        readonly object @lock = new object();
        ApplicationInstanceConfiguration? current;
        
        public ApplicationInstanceSelector(
            IApplicationInstanceStore applicationInstanceStore,
            StartUpInstanceRequest startUpInstanceRequest,
            IApplicationConfigurationContributor[] instanceStrategies,
            IOctopusFileSystem fileSystem,
            ApplicationName applicationName)
        {
            this.applicationInstanceStore = applicationInstanceStore;
            this.startUpInstanceRequest = startUpInstanceRequest;
            this.instanceStrategies = instanceStrategies;
            this.fileSystem = fileSystem;
            ApplicationName = applicationName;
        }

        public bool CanLoadCurrentInstance()
        {
            try
            {
                LoadCurrentInstance();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public ApplicationInstanceConfiguration Current => LoadCurrentInstance();

        public ApplicationName ApplicationName { get; }

        ApplicationInstanceConfiguration LoadCurrentInstance()
        {
            if (current == null)
            {
                lock (@lock)
                {
                    current ??= LoadInstance();
                }
            }

            return current;
        }

        ApplicationInstanceConfiguration LoadInstance()
        {
            var appInstance = LocateApplicationPrimaryConfiguration();
            var writableConfig = new XmlFileKeyValueStore(fileSystem, appInstance.configurationpath);
            
            var aggregatedKeyValueStore = ContributeAdditionalConfiguration(writableConfig);
            return new ApplicationInstanceConfiguration(appInstance.instanceName, appInstance.configurationpath, aggregatedKeyValueStore, writableConfig);
        }

        AggregatedKeyValueStore ContributeAdditionalConfiguration(XmlFileKeyValueStore writableConfig)
        {
            // build composite configuration pulling values out of the environment
            var keyValueStores = instanceStrategies
                .OrderBy(x => x.Priority)
                .Select(s => s.LoadContributedConfiguration())
                .WhereNotNull()
                .ToList();

            // Allow contributed values to override the core writable values.
            keyValueStores.Add(writableConfig);
            
            return new AggregatedKeyValueStore(keyValueStores.ToArray());
        }

        (string? instanceName, string configurationpath) LocateApplicationPrimaryConfiguration()
        {
            switch (startUpInstanceRequest)
            {
                case StartUpRegistryInstanceRequest registryInstanceRequest:
                {   //  `--instance` parameter provided. Use That
                    var indexInstance = applicationInstanceStore.LoadInstanceDetails(registryInstanceRequest.InstanceName);
                    return (indexInstance.InstanceName, indexInstance.ConfigurationFilePath);
                }
                case StartUpConfigFileInstanceRequest configFileInstanceRequest: 
                {   // `--config` parameter provided. Use that 
                    if (!fileSystem.FileExists(configFileInstanceRequest.ConfigFile))
                        throw new ControlledFailureException($"Specified instance config file {configFileInstanceRequest.ConfigFile} not found.");
                    return (null, configFileInstanceRequest.ConfigFile);
                }
                default:
                {   // Look in CWD for config then fallback to Default Named Instance
                    var rootPath = fileSystem.GetFullPath($"{ApplicationName}.config");
                    if (fileSystem.FileExists(rootPath))
                        return (null, rootPath);

                    try
                    {
                        var indexInstanceFallback = applicationInstanceStore.LoadInstanceDetails(null);
                        return (indexInstanceFallback.InstanceName, indexInstanceFallback.ConfigurationFilePath);
                    }
                    catch (ControlledFailureException)
                    {
                        throw new ControlledFailureException("There are no instances of OctopusServer configured on this machine. Please run the setup wizard, configure an instance using the command-line interface, specify a configuration file");
                    }
                }
            }
        }
    }
}