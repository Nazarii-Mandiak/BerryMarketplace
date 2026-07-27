namespace BerryExchange.AiCore;

public interface ITextEmbedder : IDisposable
{
    int Dimensions { get; }
    float[] Embed(string text);
}
