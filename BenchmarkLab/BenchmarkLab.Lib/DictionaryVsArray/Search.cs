namespace BenchmarkLab.DictionaryVsArray;

public static class Search
{
    public static readonly IComparer<Entity> KeyComparer = Comparer<Entity>.Create((a, b) => a.Key.CompareTo(b.Key));

    public static Entity? Linear(Entity[] a, int key)
    {
        foreach (var e in a)
        {
            if (e.Key == key) return e;
            if (e.Key > key) break;
        }
        return null;
    }

    public static Entity? Binary(Entity[] a, int key) => BinaryRange(a, key, 0, a.Length - 1);

    private static Entity? BinaryRange(Entity[] a, int key, int lo, int hi)
    {
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            int k = a[mid].Key;
            if (k == key) return a[mid];
            if (k < key) lo = mid + 1;
            else hi = mid - 1;
        }
        return null;
    }

    public static Entity? Interpolation(Entity[] a, int key)
    {
        int lo = 0, hi = a.Length - 1;
        while (lo <= hi && key >= a[lo].Key && key <= a[hi].Key)
        {
            if (a[lo].Key == a[hi].Key) return a[lo].Key == key ? a[lo] : null;
            int pos = lo + (int)(((long)key - a[lo].Key) * (hi - lo) / ((long)a[hi].Key - a[lo].Key));
            int k = a[pos].Key;
            if (k == key) return a[pos];
            if (k < key) lo = pos + 1;
            else hi = pos - 1;
        }
        return null;
    }

    public static Entity? Exponential(Entity[] a, int key)
    {
        if (a.Length == 0) return null;
        if (a[0].Key == key) return a[0];
        long bound = 1;
        while (bound < a.Length && a[(int)bound].Key < key) bound *= 2;
        return BinaryRange(a, key, (int)(bound / 2), (int)Math.Min(bound, a.Length - 1L));
    }

    public static Entity? Jump(Entity[] a, int key)
    {
        if (a.Length == 0) return null;
        int step = Math.Max(1, (int)Math.Sqrt(a.Length));
        int start = 0, end = Math.Min(step, a.Length);
        while (a[end - 1].Key < key)
        {
            start = end;
            if (start == a.Length) return null;
            end = (int)Math.Min((long)end + step, a.Length);
        }
        for (int i = start; i < end && a[i].Key <= key; i++)
            if (a[i].Key == key) return a[i];
        return null;
    }

    public static Entity? Fibonacci(Entity[] a, int key)
    {
        long f2 = 0, f1 = 1, f = 1, offset = -1;
        while (f < a.Length) { f2 = f1; f1 = f; f = f1 + f2; }
        while (f > 1)
        {
            int i = (int)Math.Min(offset + f2, a.Length - 1L);
            if (a[i].Key < key) { f = f1; f1 = f2; f2 = f - f1; offset = i; }
            else if (a[i].Key > key) { f = f2; f1 -= f2; f2 = f - f1; }
            else return a[i];
        }
        long last = offset + 1;
        return f1 != 0 && last < a.Length && a[(int)last].Key == key ? a[(int)last] : null;
    }

    // probe is immutable and prepared before timing; the comparer obeys normal ordering.
    public static Entity? Standard(Entity[] a, Entity probe)
    {
        int index = Array.BinarySearch(a, probe, KeyComparer);
        return index >= 0 ? a[index] : null;
    }
}
