using Autofac;
using MusicCrawler.Fakes;
using MusicCrawler.Lib;

namespace MusicCrawler.Backend;

// TODO: Should this be moved somewhere so that a CLI would also have access to it?
public class FakeModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterInstance(new Dictionary<ArtistKey, ArtistKey[]>
        {
            {
                new ArtistKey("Artist 1"),
                new[]
                {
                    new ArtistKey("Artist 2"),
                    new ArtistKey("Artist 3"),
                }
            }
        });
        builder.RegisterModule<LibModule>();
        builder.RegisterModule<FakesModule>();
        builder.RegisterType<HttpClient>().AsSelf().SingleInstance();
        
    }
}