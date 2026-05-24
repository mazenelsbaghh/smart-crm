using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Elastic.Clients.Elasticsearch;
using Modules.Search.Workers;

namespace Modules.Search.Application.Services
{
    public interface ISearchService
    {
        Task<IEnumerable<SearchDocument>> SearchAsync(Guid projectId, string queryText, string? entityType = null);
    }

    public class SearchService : ISearchService
    {
        private readonly ElasticsearchClient _elasticClient;
        private const string IndexName = "smart_whatsapp_search";

        public SearchService(ElasticsearchClient elasticClient)
        {
            _elasticClient = elasticClient;
        }

        public async Task<IEnumerable<SearchDocument>> SearchAsync(Guid projectId, string queryText, string? entityType = null)
        {
            try
            {
                var response = await _elasticClient.SearchAsync<SearchDocument>(s => s
                    .Index(IndexName)
                    .Query(q => q
                        .Bool(b => b
                            .Must(m => m
                                .QueryString(qs => qs
                                    .Query($"*{queryText}*")
                                    .Fields(new[] { "title", "content" })
                                )
                            )
                            .Filter(f => {
                                f.Term(t => t.Field(fld => fld.ProjectId).Value(projectId.ToString()));
                                if (!string.IsNullOrEmpty(entityType))
                                {
                                    f.Term(t => t.Field(fld => fld.EntityType).Value(entityType));
                                }
                            })
                        )
                    )
                );

                if (response.IsValidResponse)
                {
                    return response.Documents;
                }
                
                Console.WriteLine($"Elasticsearch search query returned invalid response: {response.ElasticsearchServerError?.Error}");
                return Enumerable.Empty<SearchDocument>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception querying Elasticsearch: {ex.Message}");
                return Enumerable.Empty<SearchDocument>();
            }
        }
    }
}
