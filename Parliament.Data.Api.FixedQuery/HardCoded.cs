﻿namespace Parliament.Data.Api.FixedQuery.Controllers
{
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Web.Http;
    using VDS.RDF;
    using VDS.RDF.Ontology;
    using VDS.RDF.Parsing;
    using VDS.RDF.Query;

    // TODO: Eliminate
    public class HardCoded : BaseController
    {
        public static object constituency_lookup_by_postcode(Dictionary<string, string> values)
        {
            var postcode = values["postcode"];

            var externalQueryString = Resources.GetSparql("constituency_lookup_by_postcode-external");
            var queryString = Resources.GetSparql("constituency_lookup_by_postcode");

            postcode = postcode.ToUpperInvariant().Replace(" ", string.Empty);

            GetCoordinates(postcode, externalQueryString, out string latitude, out string longitude);

            var query = new SparqlParameterizedString(queryString);

            query.SetUri("schemaUri", Global.SchemaUri);
            query.SetLiteral("longitude", longitude);
            query.SetLiteral("latitude", latitude);

            return BaseController.ExecuteSingle(query);
        }

        #region Contentful
        public static IGraph webarticle_by_id(Dictionary<string, string> values)
        {
            var webarticle_id = values["webarticle_id"];

            var uri = new Uri(Global.InstanceUri, webarticle_id).AbsoluteUri;
            var articleExists = FixedQueryController.ExecuteNamedSparql("exists", new Dictionary<string, string> { { "uri", uri } }) as SparqlResultSet;
            if (!articleExists.Result)
            {
                throw new HttpResponseException(HttpStatusCode.NotFound);
            }

            try
            {
                return Contentful.Engine.GetArticle(webarticle_id);
            }
            catch (Contentful.EntryNotFoundException)
            {
                throw new HttpResponseException(HttpStatusCode.NotFound);
            }
        }

        public static IGraph concept_by_id(Dictionary<string, string> values)
        {
            //var uri = new Uri(BaseController.Instance, topic_id).AbsoluteUri;
            // var articleExists = FixedQueryController.ExecuteNamedSparql("exists", new Dictionary<string, string> { { "uri", uri } }) as SparqlResultSet;
            //if (!articleExists.Result)
            //{
            //   throw new HttpResponseException(HttpStatusCode.NotFound);
            //}

            var concept_id = values["concept_id"];

            try
            {
                return Contentful.Engine.GetConcept(concept_id);
            }
            catch (Contentful.EntryNotFoundException)
            {
                throw new HttpResponseException(HttpStatusCode.NotFound);
            }
        }

        public static IGraph concept_index(Dictionary<string, string> values)
        {
            //var uri = new Uri(BaseController.Instance, topic_id).AbsoluteUri;
            // var articleExists = FixedQueryController.ExecuteNamedSparql("exists", new Dictionary<string, string> { { "uri", uri } }) as SparqlResultSet;
            //if (!articleExists.Result)
            //{
            //   throw new HttpResponseException(HttpStatusCode.NotFound);
            //}

            try
            {
                return Contentful.Engine.GetConcepts();
            }
            catch (Contentful.EntryNotFoundException)
            {
                throw new HttpResponseException(HttpStatusCode.NotFound);
            }
        }

        public static IGraph collection_index(Dictionary<string, string> values)
        {
            try
            {
                return Contentful.Engine.GetCollections();
            }
            catch (Contentful.EntryNotFoundException)
            {
                throw new HttpResponseException(HttpStatusCode.NotFound);
            }
        }

        public static IGraph collection_by_id(Dictionary<string, string> values)
        {
            var collection_id = values["collection_id"];

            try
            {
                return Contentful.Engine.GetCollection(collection_id);
            }
            catch (Contentful.EntryNotFoundException)
            {
                throw new HttpResponseException(HttpStatusCode.NotFound);
            }
        }
        #endregion

        public static IGraph with_schema(Dictionary<string, string> values)
        {
            // Run the original query
            var endpoint_name = values["endpoint_name"];
            var originalGraph = FixedQueryController.ExecuteNamedSparql(endpoint_name, values) as IGraph;

            // Collect types and predicates from the result of the original query
            // and get ontology information about them.
            var predicates = originalGraph.Triples.PredicateNodes;
            var types = originalGraph.GetTriplesWithPredicate(new Uri(RdfSpecsHelper.RdfType)).Select(t => t.Object);
            var ontologyResources = string.Join(" ", predicates.Union(types).Distinct().Cast<IUriNode>().Select(p => "<" + p.Uri.AbsoluteUri + ">"));
            var ontologyQuery = @"
PREFIX owl: <http://www.w3.org/2002/07/owl#>
PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>

CONSTRUCT {
	?ontology ?ontologyProperty ?ontologyValue .

	?ontologyResource ?resourceProperty ?resourceValue .
}
FROM <http://www.ontotext.com/explicit>
WHERE {
    VALUES ?ontologyResource {@ontologyResources}

	?ontology ?ontologyProperty ?ontologyValue .

	?ontologyResource
		?resourceProperty ?resourceValue ;
		rdfs:isDefinedBy ?ontology.

    FILTER(?resourceProperty != owl:inverseOf)
}".Replace("@ontologyResources", ontologyResources);

            var ontologyGraph = BaseController.ExecuteList(new SparqlParameterizedString(ontologyQuery)) as IGraph;

            // Collect classes from across both the result of the original query and the ontology generated for it
            // (could be types from the original, classes from the ontology or domains and ranges from the ontology)
            // and see if any are sub-classes of any other
            var domains = ontologyGraph.GetTriplesWithPredicate(ontologyGraph.CreateUriNode(new Uri(OntologyHelper.PropertyDomain))).Select(t => t.Object);
            var ranges = ontologyGraph.GetTriplesWithPredicate(ontologyGraph.CreateUriNode(new Uri(OntologyHelper.PropertyRange))).Select(t => t.Object);
            var classes = ontologyGraph.GetTriplesWithPredicateObject(ontologyGraph.CreateUriNode(new Uri(RdfSpecsHelper.RdfType)), ontologyGraph.CreateUriNode(new Uri(OntologyHelper.OwlClass))).Select(t => t.Subject);
            var classResources = string.Join(" ", domains.Union(ranges).Union(classes).Union(types).Distinct().Cast<IUriNode>().Select(p => "<" + p.Uri.AbsoluteUri + ">"));
            var subClassQuery = @"
PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>

CONSTRUCT {
	?class rdfs:subClassOf ?superClass .
}
FROM <http://www.ontotext.com/explicit>
WHERE {
    VALUES ?class {@classResources}
    VALUES ?superClass {@classResources}

	?class rdfs:subClassOf ?superClass .
}".Replace("@classResources", classResources);

            var subClassGraph = BaseController.ExecuteList(new SparqlParameterizedString(subClassQuery)) as IGraph;

            // The result is all three graphs above merged.
            var resultGraph = new Graph();
            resultGraph.Merge(originalGraph);
            resultGraph.Merge(ontologyGraph);
            resultGraph.Merge(subClassGraph);
            return resultGraph;
        }

        private static void GetCoordinates(string postcode, string externalQueryString, out string latitude, out string longitude)
        {
            if (postcode.StartsWith("BT"))
            {
                using (var client = new HttpClient())
                {
                    try
                    {
                        var json = client.GetStringAsync($"http://api.postcodes.io/postcodes/{postcode}").Result;

                        dynamic postcodesioJson = JsonConvert.DeserializeObject(json);

                        latitude = postcodesioJson.result.latitude;
                        longitude = postcodesioJson.result.longitude;
                    }
                    catch (AggregateException e) when (e.InnerException is HttpRequestException)
                    {
                        throw new HttpResponseException(HttpStatusCode.NotFound);
                    }
                }
            }
            else
            {
                var externalQuery = new SparqlParameterizedString(externalQueryString);
                externalQuery.SetUri("postcode", new Uri(new Uri("http://data.ordnancesurvey.co.uk/id/postcodeunit/"), postcode));
                var externalResults = BaseController.ExecuteList(externalQuery, "http://data.ordnancesurvey.co.uk/datasets/os-linked-data/apis/sparql") as IGraph;

                if (externalResults.Triples.Any())
                {
                    var longitudeObject = (LiteralNode)externalResults.GetTriplesWithPredicate(new Uri("http://www.w3.org/2003/01/geo/wgs84_pos#long")).SingleOrDefault().Object;
                    var latitudeObject = (LiteralNode)externalResults.GetTriplesWithPredicate(new Uri("http://www.w3.org/2003/01/geo/wgs84_pos#lat")).SingleOrDefault().Object;

                    longitude = longitudeObject.Value;
                    latitude = latitudeObject.Value;
                }
                else
                {
                    throw new HttpResponseException(HttpStatusCode.NotFound);
                }
            }
        }
    }
}
