﻿using Autofac;
using MusicCrawler.MongoDB.Services.Data;
using MusicCrawler.MongoDB.Services.Singletons;
using Noggog.Autofac;

namespace MusicCrawler.MongoDB;

public class MongoDbDataModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterAssemblyTypes(typeof(RecommendationPersistanceRepo).Assembly)
            .InNamespacesOf(
                typeof(RecommendationPersistanceRepo))
            .AsImplementedInterfaces()
            .AsSelf()
            .SingleInstance();
    }
}