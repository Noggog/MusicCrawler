using MusicCrawler.Lib;
using MusicCrawler.Lib.Services.Singletons;

namespace MusicCrawler.Backend;

public class Query
{
    /**
    * Example graphql query:
query {
    playground(a: 2)
}
     */
    public string Playground(int a)
    {
        return $"a:{a}";
    }

    /**
    * Example graphql query:
query {
     recommendations {
         key {
             artistName
         }
         sourceArtists {
             artistName
         }
     }
 }
     */
    public async Task<List<Recommendation>> Recommendations([Service] RecommendationInteractor recommendationInteractor)
    {
        return (await recommendationInteractor.Recommendations()).ToList();
    }

    // TODO: GraphQL would not accept "Unit" or nothing as a response, so I am returning a boolean that always returns true. Is there a better way?
    public async Task<Boolean> AccumulateRecommendations([Service] RecommendationInteractor recommendationInteractor)
    {
        await recommendationInteractor.AccumulateRecommendations();
        return true;
    }
}

public class QueryType : ObjectType<Query>
{
    protected override void Configure(IObjectTypeDescriptor<Query> descriptor)
    {
        descriptor.Field(x => x.Playground(default))
            .Argument("a", arg => arg.Type<IntType>())
            .Type<StringType>();

        descriptor.Field(x => x.Recommendations(default));
        descriptor.Field(x => x.AccumulateRecommendations(default));
    }
}