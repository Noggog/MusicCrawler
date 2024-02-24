using AutoFixture;
using AutoFixture.AutoNSubstitute;
using AutoFixture.Xunit2;
using Noggog.Testing.AutoFixture;

namespace MusicCrawler.Tests;

public class FakeData : AutoDataAttribute
{
    public FakeData(
        bool RegisterAppFakes = true,
        bool ConfigureMembers = false,
        bool UseMockFileSystem = true,
        bool GenerateDelegates = false,
        bool OmitAutoProperties = false)
        : base(() =>
        {
            var ret = new Fixture();
            ret.Customize(new AutoNSubstituteCustomization()
            {
                ConfigureMembers = ConfigureMembers,
                GenerateDelegates = GenerateDelegates
            });
            ret.Customize(new DefaultCustomization(UseMockFileSystem));
            ret.OmitAutoProperties = OmitAutoProperties;
            ret.Customizations.Add(new FakeBuilder(RegisterAppFakes));
            return ret;
        })
    {
    }
}