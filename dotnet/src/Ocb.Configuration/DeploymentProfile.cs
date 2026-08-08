namespace Ocb.Configuration;

public enum DeploymentProfile
{
    Singlebox,
    Cluster,
    Test
}

public enum VectorProvider
{
    SonnetDb,
    Qdrant,
    InMemory
}
