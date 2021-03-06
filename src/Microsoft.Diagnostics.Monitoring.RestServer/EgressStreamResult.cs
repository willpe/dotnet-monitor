﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.RestServer
{
    internal class EgressStreamResult : ActionResult
    {
        private readonly Func<IEgressService, CancellationToken, Task<EgressResult>> _egress;
        private readonly KeyValueLogScope _scope;

        public EgressStreamResult(Func<CancellationToken, Task<Stream>> action, string endpointName, string artifactName, IEndpointInfo source, string contentType, KeyValueLogScope scope)
        {
            _egress = (service, token) => service.EgressAsync(endpointName, action, artifactName, contentType, source, token);
            _scope = scope;
        }

        public EgressStreamResult(Func<Stream, CancellationToken, Task> action, string endpointName, string artifactName, IEndpointInfo source, string contentType, KeyValueLogScope scope)
        {
            _egress = (service, token) => service.EgressAsync(endpointName, action, artifactName, contentType, source, token);
            _scope = scope;
        }

        public override async Task ExecuteResultAsync(ActionContext context)
        {
            ILogger<EgressStreamResult> logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger<EgressStreamResult>();

            using var _ = logger.BeginScope(_scope);

            await context.InvokeAsync(async (token) =>
            {
                IEgressService egressService = context.HttpContext.RequestServices
                    .GetRequiredService<IEgressService>();

                EgressResult egressResult = await _egress(egressService, token);

                logger.EgressedArtifact(egressResult.Value);

                // The remaining code is creating a JSON object with a single property and scalar value
                // that indiates where the stream data was egressed. Because the name of the artifact is
                // automatically generated by the REST API and the caller of the endpoint might not know
                // the specific configuration information for the egress provider, this value allows the
                // caller to more easily find the artifact after egress has completed.
                IDictionary<string, string> data = new Dictionary<string, string>(StringComparer.Ordinal);
                data.Add(egressResult.Name, egressResult.Value);

                ActionResult jsonResult = new JsonResult(data);
                await jsonResult.ExecuteResultAsync(context);
            }, logger);
        }
    }
}
