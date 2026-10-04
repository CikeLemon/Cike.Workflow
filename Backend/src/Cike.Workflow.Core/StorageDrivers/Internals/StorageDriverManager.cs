using Cike.Core.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cike.Workflow.Core.StorageDrivers.Internals;

internal class StorageDriverManager(IStorageDriverRegistry storageDriverRegistry, IServiceProvider serviceProvider) : IStorageDriverManager, IScopedDependency
{
    public IStorageDriver? Find(string type)
    {
        if (type.IsNullOrEmpty())
        {
            return null;
        }
        var descriptor = storageDriverRegistry.Find(type);
        return descriptor?.Factory(serviceProvider);
    }
}
