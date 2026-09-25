using Beaver.Infrastructure.Domain.Shared.Stimulus;
using Cike.Workflow.Core.Activities.Abstracts;
using Cike.Workflow.Core.Attributes;
using Cike.Workflow.Core.Contexts;
using Cike.Workflow.Core.Contexts.Models;
using Cike.Workflow.Core.Models;
using Cike.Workflow.Expressions.Models;
using Cike.Workflow.Http.ContentWriters;
using Cike.Workflow.Http.Extensions;
using Cike.Workflow.Http.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Cike.Workflow.Http.Activities
{
    [Activity(Category = "Http", Icon = "globe")]
    public class SendHttpRequest : Activity
    {
        /// <summary>
        /// 默认错误状态码列表：罗列常见 4xx/5xx，按前缀匹配命中即抛异常。
        /// </summary>
        private static List<int> DEFAULT_RESPONSE_ERROR_CODE_RANGE = new([400, 401, 402, 403, 404, 405, 406, 407, 408, 409, 410, 411, 412, 413, 414, 415, 416, 417, 418, 421, 422, 423, 424, 425, 426, 428, 429, 431, 451, 500, 501, 502, 503, 504, 505, 506, 507, 508, 510, 511]);

        [Input(Order = 0)] public Input<Uri?> Url { get; set; } = null!;

        [Input(
            Description = "The HTTP method to use when sending the request.",
            Options = new[]
            {
            "GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS", "HEAD"
            },
            DefaultValue = "GET",
            Order = 1
        )]
        public Input<string> Method { get; set; } = new("GET");

        [Input(
            Description = "请求超时时间（秒），超时后按异常策略处理",
            DefaultValue = 60,
            Order = 7
        )]
        public Input<int> TimeoutInterval { get; set; } = new(60);

        [Input(
            Description = "The content to send with the request. Can be a string, an object, a byte array or a stream.",
            Order = 2
            )]
        public Input<object?> Content { get; set; } = null!;

        [Input(
            Description = "The content type to use when sending the request.",
            Order = 3
        )]
        public Input<string?> ContentType { get; set; } = null!;

        [Input(
            Description = "The Authorization header value to send with the request. For example: Bearer {some-access-token}",
            Category = "Security",
            CanContainSecrets = true,
            Order = 4
        )]
        public Input<string?> Authorization { get; set; } = null!;

        [Input(
            Description = "The headers to send along with the request.",
            Category = "Advanced",
            Order = 6
        )]
        public Input<CustomHttpHeaders?> RequestHeaders { get; set; } = new(new CustomHttpHeaders());

        [Input(Description = "Wait for the child workflow to complete before completing this activity.")]
        public Input<bool> WaitForCompletion { get; set; } = null!;

        [Input(
            Description = "The HTTP status codes that trigger async suspension when WaitForCompletion is true. Defaults to 202.",
            DefaultValue = new[] { 202 },
            Category = "Advanced"
        )]
        public Input<List<int>> WaitForCompletionOnStatusCodes { get; set; } = new([202]);

        [Input(
            Description = "响应错误状态码范围，匹配开头即抛出异常，多个用逗号分隔。",
            Category = "Advanced",
            Order = 8
        )]
        public Input<List<int>> ResponseErrorCodes { get; set; } = new(DEFAULT_RESPONSE_ERROR_CODE_RANGE);

        [Output(Description = "The HTTP response status code")]
        public Output<int> StatusCode { get; set; } = null!;

        [Output(Description = "The parsed content, if any.")]
        public Output<object?> ParsedContent { get; set; } = null!;

        [Output(Description = "The response headers that were received.")]
        public Output<CustomHttpHeaders?> ResponseHeaders { get; set; } = null!;

        /// <summary>
        /// The payload returned by the async callback when WaitForCompletion is true.
        /// </summary>
        [Output(Description = "The payload returned by the async callback.")]
        public Output<object?> CallbackPayload { get; set; } = null!;

        protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
        {
            var logger = (ILogger)context.GetRequiredService(typeof(ILogger<>).MakeGenericType(GetType()));
            var httpClientFactory = context.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(SendHttpRequest));
            var cancellationToken = context.CancellationToken;
            var timeoutInterval = TimeoutInterval.GetOrDefault(context);

            if (timeoutInterval > 0)
            {
                httpClient.Timeout = TimeSpan.FromSeconds(timeoutInterval);
            }

            var response = await SendRequestAsyncCore(httpClient, context, cancellationToken);

            var parsedContent = await ParseContentAsync(context, response);
            var statusCode = (int)response.StatusCode;
            var responseHeaders = new CustomHttpHeaders(response.Headers);


            context.Set(ParsedContent, parsedContent);
            context.Set(StatusCode, statusCode);
            context.Set(ResponseHeaders, responseHeaders);

            var errorStatusCodes = ResponseErrorCodes.GetOrDefault(context) ?? [.. DEFAULT_RESPONSE_ERROR_CODE_RANGE];
            if (errorStatusCodes.Any(c => c == statusCode))
                throw new HttpRequestException($"HTTP 请求失败，状态码：{statusCode}", null, response.StatusCode);

            var waitForCompletion = WaitForCompletion.GetOrDefault(context);
            var suspendOnStatusCodes = WaitForCompletionOnStatusCodes.GetOrDefault(context) ?? [202];

            if (waitForCompletion && suspendOnStatusCodes.ToList().Contains(statusCode))
                SuspendAndWait(context);
            else
                await HandleResponseAsync(context, response);
        }

        protected async ValueTask HandleResponseAsync(ActivityExecutionContext context, HttpResponseMessage response)
        {
            await context.CompleteActivityAsync();
        }

        protected ValueTask OnHttpCallbackAsync(ActivityExecutionContext context)
        {
            context.Set(CallbackPayload, context.WorkflowExecutionContext.Input);
            return context.CompleteActivityAsync();
        }

        private HttpRequestMessage PrepareRequest(ActivityExecutionContext context)
        {
            var method = Method.GetOrDefault(context) ?? "GET";
            var url = Url.Get(context);
            var request = new HttpRequestMessage(new HttpMethod(method), url);
            var headers = context.GetHeaders(RequestHeaders);
            var authorization = Authorization.GetOrDefault(context);

            if (!string.IsNullOrWhiteSpace(authorization))
                request.Headers.Authorization = System.Net.Http.Headers.AuthenticationHeaderValue.Parse(authorization);

            foreach (var header in headers)
                request.Headers.Add(header.Key, header.Value.AsEnumerable());

            var contentType = ContentType.GetOrDefault(context);
            var content = Content.GetOrDefault(context);

            if (contentType != null && content != null)
            {
                var factories = context.WorkflowExecutionContext.ServiceProvider.GetServices<IHttpContentFactory>();
                var factory = SelectContentWriter(contentType, factories);
                request.Content = factory.CreateHttpContent(content, contentType);
            }

            var waitForCompletion = WaitForCompletion.GetOrDefault(context);
            if (waitForCompletion)
            {
                request.Headers.TryAddWithoutValidation("X-Beaver-Workflow-Instance-Id", context.WorkflowExecutionContext.Id.ToString());
                request.Headers.TryAddWithoutValidation("X-Beaver-Activity-Instance-Id", context.Id.ToString());
            }

            // 调试（试跑）实例的出站请求统一打标，由下游服务自行识别处置
            if (context.WorkflowExecutionContext.IsDebug)
                request.Headers.TryAddWithoutValidation("isDebug", "true");

            return request;
        }

        private async Task<HttpResponseMessage> SendRequestAsyncCore(HttpClient client, ActivityExecutionContext ctx, CancellationToken ct)
        {
            var request = PrepareRequest(ctx);
            return await client.SendAsync(request, ct);
        }

        private void SuspendAndWait(ActivityExecutionContext context)
        {
            context.CreateBookmark(new CreateBookmarkArgs
            {
                Callback = OnHttpCallbackAsync,
                BookmarkName = nameof(SuspendStimulus),
                Stimulus = new SuspendStimulus(context.Id),
                IncludeActivityInstanceId = true
            });
        }


        private async Task<object?> ParseContentAsync(ActivityExecutionContext context, HttpResponseMessage httpResponse)
        {
            var httpContent = httpResponse.Content;
            if (!HasContent(httpContent))
                return null;

            var cancellationToken = context.CancellationToken;
            var targetType = ParsedContent.GetTargetType(context);
            var contentStream = await httpContent.ReadAsStreamAsync(cancellationToken);
            var responseHeaders = httpResponse.Headers;
            var contentHeaders = httpContent.Headers;
            var contentType = contentHeaders.ContentType?.MediaType ?? "application/octet-stream";

            targetType ??= contentType switch
            {
                "application/json" => typeof(object),
                _ => typeof(string)
            };

            var contentHeadersDictionary = contentHeaders.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
            var responseHeadersDictionary = responseHeaders.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
            var headersDictionary = contentHeadersDictionary.Concat(responseHeadersDictionary).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            return await context.ParseContentAsync(contentStream, contentType, targetType, headersDictionary, cancellationToken);
        }

        private static bool HasContent(HttpContent httpContent) => httpContent.Headers.ContentLength > 0;

        private IHttpContentFactory SelectContentWriter(string? contentType, IEnumerable<IHttpContentFactory> factories)
        {
            if (string.IsNullOrWhiteSpace(contentType))
                return new JsonContentFactory();

            var parsedContentType = new System.Net.Mime.ContentType(contentType);
            return factories.FirstOrDefault(httpContentFactory => httpContentFactory.SupportedContentTypes.Any(c => c == parsedContentType.MediaType)) ?? new JsonContentFactory();
        }
    }
}
