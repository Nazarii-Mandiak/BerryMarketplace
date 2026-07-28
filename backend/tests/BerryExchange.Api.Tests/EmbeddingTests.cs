using BerryExchange.AiCore;

namespace BerryExchange.Api.Tests;

public class EmbeddingTests
{
    [Fact]
    public void Embeddings_are_384_dimensional_and_rank_similar_text_closer()
    {
        using var embedder = new LocalTextEmbedder();
        var strawberries = embedder.Embed("sweet ripe strawberries for jam");
        var strawberries2 = embedder.Embed("fresh strawberry pints, very sweet");
        var tractors = embedder.Embed("used diesel tractor parts catalog");

        Assert.Equal(384, embedder.Dimensions);
        Assert.Equal(384, strawberries.Length);
        Assert.True(Cosine(strawberries, strawberries2) > Cosine(strawberries, tractors),
            "similar berry texts should be closer than unrelated text");
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
