﻿using Rocket.API;
using Rocket.API.DependencyInjection;
using Rocket.Tests;

namespace Rocket.Properties
{
    public class DependencyRegistrator : IDependencyRegistrator
    {
        public void Register(IDependencyContainer container, IDependencyResolver resolver)
        {
            container.RegisterSingletonType<IImplementation, Implementation>();
        }
    }
}
