using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Ocb.PluginApi.Vector;

namespace Ocb.Contracts.Tests.PluginApi;

[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class VectorContractsTests
{
    [Fact]
    public void VectorStore_MustNotExposeEmbeddingMethods()
    {
        var methods = typeof(IVectorStore).GetMethods().Select(x => x.Name).ToArray();
        Assert.DoesNotContain("EmbedAsync", methods);
        Assert.DoesNotContain("RerankAsync", methods);
    }

    [Fact]
    public void VectorStore_MustBe_Interface()
    {
        Assert.True(typeof(IVectorStore).IsInterface);
    }

    [Fact]
    public void HybridSearchStore_MustBe_Interface()
    {
        Assert.True(typeof(IHybridSearchStore).IsInterface);
    }

    [Fact]
    public void VectorStoreAdministration_MustBe_Interface()
    {
        Assert.True(typeof(IVectorStoreAdministration).IsInterface);
    }

    [Fact]
    public void EmbeddingPlugin_MustBe_SeparateFromVectorStore()
    {
        // IEmbeddingPlugin is a distinct interface from IVectorStore
        Assert.True(typeof(IEmbeddingPlugin).IsInterface);
        Assert.False(typeof(IEmbeddingPlugin).IsAssignableFrom(typeof(IVectorStore)));
        Assert.False(typeof(IVectorStore).IsAssignableFrom(typeof(IEmbeddingPlugin)));
    }

    [Fact]
    public void RerankerPlugin_MustBe_SeparateFromVectorStore()
    {
        // IRerankerPlugin is a distinct interface from IVectorStore
        Assert.True(typeof(IRerankerPlugin).IsInterface);
        Assert.False(typeof(IRerankerPlugin).IsAssignableFrom(typeof(IVectorStore)));
        Assert.False(typeof(IVectorStore).IsAssignableFrom(typeof(IRerankerPlugin)));
    }

    [Fact]
    public void IVectorStore_Methods_UseValueTask()
    {
        var methods = typeof(IVectorStore).GetMethods();
        foreach (var method in methods)
        {
            Assert.True(
                method.ReturnType == typeof(ValueTask) ||
                (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>)),
                $"{method.Name} must return ValueTask or ValueTask<T>");
        }
    }

    [Fact]
    public void IEmbeddingPlugin_Methods_UseValueTask()
    {
        var methods = typeof(IEmbeddingPlugin).GetMethods();
        foreach (var method in methods)
        {
            Assert.True(
                method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>),
                $"{method.Name} must return ValueTask<T>");
        }
    }

    [Fact]
    public void VectorRecord_ContainsRequiredFields()
    {
        var record = new VectorRecord("t1", "id1", "m1", 1024, new float[1024], null);
        Assert.Equal("t1", record.TenantId);
        Assert.Equal("id1", record.VectorId);
        Assert.Equal("m1", record.Model);
        Assert.Equal(1024, record.Dimension);
    }

    [Fact]
    public void VectorCompatibilityException_IsException()
    {
        Assert.True(typeof(VectorCompatibilityException).IsAssignableTo(typeof(Exception)));
    }
}
