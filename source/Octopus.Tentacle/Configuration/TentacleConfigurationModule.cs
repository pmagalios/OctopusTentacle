using System;
using Autofac;

namespace Octopus.Tentacle.Configuration
{
    public class TentacleConfigurationModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            base.Load(builder);

            builder.RegisterType<TentacleConfiguration>().As<ITentacleConfiguration>().SingleInstance();
        }
    }
}