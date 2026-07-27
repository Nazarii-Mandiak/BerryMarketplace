using SmartComponents.LocalEmbeddings;

namespace BerryExchange.AiCore;

// Local ONNX embedding model (no network, no API key). The same class is used by
// the API (query-time) and the AiWorker (index-time) so vectors are comparable.
public sealed class LocalTextEmbedder : ITextEmbedder
{
    private readonly LocalEmbedder _embedder = new();
    public int Dimensions => 384;
    public float[] Embed(string text) => _embedder.Embed(text).Values.ToArray();
    public void Dispose() => _embedder.Dispose();
}
