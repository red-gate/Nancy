﻿namespace Nancy.Owin
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;

    using Nancy.Bootstrapper;
    using Nancy.IO;

    /// <summary>
    /// Nancy host for OWIN hosts
    /// </summary>
    public class NancyOwinHost
    {
        private readonly INancyEngine engine;

        public const string RequestEnvironmentKey = "OWIN_REQUEST_ENVIRONMENT";

        /// <summary>
        /// Initializes a new instance of the <see cref="NancyOwinHost"/> class.
        /// </summary>
        /// <param name="next">Next middleware to run if necessary</param>
        /// <param name="bootstrapper">The bootstrapper that should be used by the host.</param>
        public NancyOwinHost(Func<IDictionary<string, object>, Task> next, INancyBootstrapper bootstrapper)
        {
            bootstrapper.Initialise();

            this.engine = bootstrapper.GetEngine();
        }

        /// <summary>
        /// OWIN App Action
        /// </summary>
        /// <param name="environment">Application environment</param>
        /// <returns>Returns result</returns>
        public Task Invoke(IDictionary<string, object> environment)
        {
            var owinRequestMethod = Get<string>(environment, "owin.RequestMethod");
            var owinRequestScheme = Get<string>(environment, "owin.RequestScheme");
            var owinRequestHeaders = Get<IDictionary<string, string[]>>(environment, "owin.RequestHeaders");
            var owinRequestPathBase = Get<string>(environment, "owin.RequestPathBase");
            var owinRequestPath = Get<string>(environment, "owin.RequestPath");
            var owinRequestQueryString = Get<string>(environment, "owin.RequestQueryString");
            var owinRequestBody = Get<Stream>(environment, "owin.RequestBody");
            var serverClientIp = Get<string>(environment, "server.RemoteIpAddress");
            //var callCancelled = Get<CancellationToken>(environment, "owin.RequestBody");

            var url = new Url
            {
                Scheme = owinRequestScheme,
                HostName = GetHeader(owinRequestHeaders, "Host"),
                Port = null,
                BasePath = owinRequestPathBase,
                Path = owinRequestPath,
                Query = owinRequestQueryString,
            };

            var nancyRequestStream = new RequestStream(owinRequestBody, ExpectedLength(owinRequestHeaders), false);

            var nancyRequest = new Request(
                    owinRequestMethod,
                    url,
                    nancyRequestStream,
                    owinRequestHeaders.ToDictionary(kv => kv.Key, kv => (IEnumerable<string>)kv.Value, StringComparer.OrdinalIgnoreCase),
                    serverClientIp);

            var tcs = new TaskCompletionSource<int>();

            this.engine.HandleRequest(
                nancyRequest,
                StoreEnvironment(environment),
                RequestComplete(environment, tcs), 
                RequestErrored(tcs));

            return tcs.Task;
        }

        /// <summary>
        /// Gets a delegate to store the OWIN environment into the NancyContext
        /// </summary>
        /// <param name="environment">OWIN Environment</param>
        /// <returns>Delegate</returns>
        private static Func<NancyContext, NancyContext> StoreEnvironment(IDictionary<string, object> environment)
        {
            return context =>
                {
                    environment["nancy.NancyContext"] = context;
                    context.Items[RequestEnvironmentKey] = environment;
                    return context;
                };
        }

        /// <summary>
        /// Gets a delegate to handle converting a nancy response
        /// to the format required by OWIN and signals that the we are
        /// now complete.
        /// </summary>
        /// <param name="environment">OWIN environment</param>
        /// <param name="tcs">The task completion source to signal</param>
        /// <returns>Delegate</returns>
        private static Action<NancyContext> RequestComplete(IDictionary<string, object> environment, TaskCompletionSource<int> tcs)
        {
            return context =>
                {
                    var owinResponseHeaders = Get<IDictionary<string, string[]>>(environment, "owin.ResponseHeaders");
                    var owinResponseBody = Get<Stream>(environment, "owin.ResponseBody");

                    var nancyResponse = context.Response;
                    environment["owin.ResponseStatusCode"] = (int)nancyResponse.StatusCode;

                    foreach (var responseHeader in nancyResponse.Headers)
                    {
                        owinResponseHeaders[responseHeader.Key] = new[] { responseHeader.Value };
                    }

                    if (!string.IsNullOrWhiteSpace(nancyResponse.ContentType))
                    {
                        owinResponseHeaders["Content-Type"] = new[] { nancyResponse.ContentType };
                    }

                    if (nancyResponse.Cookies != null && nancyResponse.Cookies.Count != 0)
                    {
                        owinResponseHeaders["Set-Cookie"] =
                            nancyResponse.Cookies.Select(cookie => cookie.ToString()).ToArray();
                    }

                    nancyResponse.Contents(owinResponseBody);

                    context.Dispose();

                    tcs.TrySetResult(0);
                };
        }

        /// <summary>
        /// Gets a delegate to handle request errors
        /// </summary>
        /// <param name="tcs">Completion source to signal</param>
        /// <returns>Delegate</returns>
        private Action<Exception> RequestErrored(TaskCompletionSource<int> tcs)
        {
            return tcs.SetException;
        }

        private static T Get<T>(IDictionary<string, object> env, string key)
        {
            object value;
            return env.TryGetValue(key, out value) && value is T ? (T)value : default(T);
        }

        private static string GetHeader(IDictionary<string, string[]> headers, string key)
        {
            string[] value;
            return headers.TryGetValue(key, out value) && value != null ? string.Join(",", value.ToArray()) : null;
        }

        private static long ExpectedLength(IDictionary<string, string[]> headers)
        {
            var header = GetHeader(headers, "Content-Length");
            if (string.IsNullOrWhiteSpace(header))
                return 0;

            int contentLength;
            return int.TryParse(header, NumberStyles.Any, CultureInfo.InvariantCulture, out contentLength) ? contentLength : 0;
        }
    }
}
