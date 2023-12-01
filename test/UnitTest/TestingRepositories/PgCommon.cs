using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories
{
    public class PgCommon
    {
        public IInstanceRepository InstanceRepo { get; set; }

        public IDataRepository DataRepo { get; set; }

        public PgCommon()
        {
            var serviceList = ServiceUtil.GetServices(new List<Type>() { typeof(IInstanceRepository), typeof(IDataRepository) });
            InstanceRepo = (IInstanceRepository)serviceList.First(i => i.GetType() == typeof(PgInstanceRepository));
            DataRepo = (IDataRepository)serviceList.First(i => i.GetType() == typeof(PgDataRepository));
        }
    }
}
