using Cike.Core.Modularity;
using Cike.Workflow.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Cike.Workflow.Http
{
    [DependsOn([
        typeof(CikeWorkflowCoreModule)
        ])]
    public class CikeWorkflowHttpModule : CikeModule
    {
        public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
        {
            // SendHttpRequest 依赖 IHttpClientFactory（HttpClient 工厂由本模块的出站活动消费）
            context.Services.AddHttpClient();
            return base.ConfigureServicesAsync(context);
        }
    }
}
