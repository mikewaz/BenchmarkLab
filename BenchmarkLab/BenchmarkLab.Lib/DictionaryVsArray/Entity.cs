namespace BenchmarkLab.DictionaryVsArray;

public sealed class Entity
{
    public readonly int Key;
    public readonly int Value;

    public Entity(int key)
    {
        Key = key;
        Value = new Random().Next();
    }

    public Entity(int key, int value)
    {
        Key = key;
        Value = value;
    }
}
