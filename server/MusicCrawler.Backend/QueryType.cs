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
}

public class QueryType : ObjectType<Query>
{
    protected override void Configure(IObjectTypeDescriptor<Query> descriptor)
    {
        descriptor.Field(x => x.Playground(default))
            .Argument("a", arg => arg.Type<IntType>())
            .Type<StringType>();

        descriptor.Field(x => x.Recommendations(default));
    }
}