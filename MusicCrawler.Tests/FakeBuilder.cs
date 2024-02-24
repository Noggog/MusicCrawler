using AutoFixture.Kernel;
using MusicCrawler.Fakes.Services.Singletons;
using MusicCrawler.Lib;

namespace MusicCrawler.Tests;

public class FakeBuilder : ISpecimenBuilder
{
    private readonly bool _registerAppFakes;

    public FakeBuilder(bool registerAppFakes)
    {
        _registerAppFakes = registerAppFakes;
    }
    
    
    public object Create(object request, ISpecimenContext context)
    {
        if (request is Type t)
        {
            if (t == typeof(ILibraryQuery))
            {
                return new FakeLibraryQuery();
            }
            else if (t == typeof(IRecommendationRepo))
            {
                return new FakeRecommendationRepo();
            }
        }

        return new NoSpecimen();
    }
}