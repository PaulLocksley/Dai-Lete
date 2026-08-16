var builder = DistributedApplication.CreateBuilder(args);

var podcastStoragePath = Path.GetFullPath("../Podcasts");
Directory.CreateDirectory(podcastStoragePath);
var databasePath = Path.Combine(podcastStoragePath, "Podcasts.sqlite");

var valkey = builder.AddValkey("valkey")
    .WithDataVolume();

builder.AddProject<Projects.Dai_Lete>("frontend")
    .WithReference(valkey)
    .WaitFor(valkey)
    .WithExternalHttpEndpoints()
    .WithEnvironment("Worker__Enabled", "false")
    .WithEnvironment("Valkey__ConnectionString", valkey.Resource.ConnectionStringExpression)
    .WithEnvironment("podcastStoragePath", podcastStoragePath)
    .WithEnvironment("Database__Path", databasePath);

builder.AddProject<Projects.Dai_Lete>("worker")
    .WithReference(valkey)
    .WaitFor(valkey)
    .WithEnvironment("Worker__Enabled", "true")
    .WithEnvironment("Valkey__ConnectionString", valkey.Resource.ConnectionStringExpression)
    .WithEnvironment("podcastStoragePath", podcastStoragePath)
    .WithEnvironment("Database__Path", databasePath);

builder.Build().Run();
