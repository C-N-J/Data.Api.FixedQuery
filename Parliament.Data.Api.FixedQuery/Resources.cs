﻿namespace Parliament.Data.Api.FixedQuery
{
    using Microsoft.OpenApi.Any;
    using Microsoft.OpenApi.Models;
    using Microsoft.OpenApi.Readers;
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;

    public static class Resources
    {
        private const string BaseName = "Parliament.Data.Api.FixedQuery";

        public static string OpenApiDefinitionResourceName
        {
            get
            {
                return $"{BaseName}.openapi.json";
            }
        }

        private static OpenApiDocument openApiDefinition = null;
        public static OpenApiDocument OpenApiDefinition
        {
            get
            {
                if (openApiDefinition == null)
                    using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(OpenApiDefinitionResourceName))
                    {
                        apiDiagnostic = new OpenApiDiagnostic();
                        openApiDefinition = new OpenApiStreamReader().Read(stream, out apiDiagnostic);                        
                    }
                return openApiDefinition;
            }
        }

        private static OpenApiDiagnostic apiDiagnostic = null;
        public static OpenApiDiagnostic ApiDiagnostic
        {
            get
            {
                return apiDiagnostic;
            }
        }


        public static OpenApiPathItem GetApiPathItem(string endpointName)
        {
            string key = $"/{endpointName}{{ext}}";
            if (OpenApiDefinition.Paths.ContainsKey(key))
                return OpenApiDefinition.Paths[key];
            else
                return null;
        }

        public static EndpointType GetEndpointType(OpenApiPathItem openApiPath)
        {
            string xType = ((OpenApiString)openApiPath.Extensions["x-type"]).Value;
            return (EndpointType)Enum.Parse(typeof(EndpointType), xType, true);
        }

        public static ParameterType GetParameterType(OpenApiParameter openApiParameter)
        {
            string xType = ((OpenApiString)openApiParameter.Extensions["x-type"]).Value;
            return (ParameterType)Enum.Parse(typeof(ParameterType), xType, true);
        }

        public static IEnumerable<OpenApiParameter> GetSparqlParameters(OpenApiPathItem openApiPath)
        {
            return openApiPath.Operations[OperationType.Get].Parameters
                .Where(p => p.Name != "ext" && p.Name != "format");
        }

        public static string GetSparql(string name)
        {
            return Resources.GetFile($"{BaseName}.Sparql.{name}.sparql");
        }

        public static IEnumerable<string> SparqlFileNames
        {
            get
            {
                var prefix = $"{BaseName}.Sparql.";
                var suffix = ".sparql";

                return Assembly.GetExecutingAssembly()
                    .GetManifestResourceNames()
                    .Where(resourceName => resourceName.StartsWith(prefix))
                    .Where(resourceName => resourceName.EndsWith(suffix))
                    .Select(resourceName => resourceName.Split(new[] { prefix, suffix }, StringSplitOptions.None))
                    .Select(components => components[1])
                    .Except(new[] { "constituency_lookup_by_postcode-external" });
            }
        }

        private static string GetFile(string resourceName)
        {
            using (var sparqlResourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                using (var reader = new StreamReader(sparqlResourceStream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}