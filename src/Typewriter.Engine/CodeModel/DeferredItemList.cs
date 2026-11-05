using System.Collections;

namespace Typewriter.CodeModel;

internal sealed class DeferredItemList<T> : IReadOnlyList<T>
    where T : Item
{
    private readonly Lazy<T>[] _items;

    private DeferredItemList(Lazy<T>[] items)
    {
        _items = items;
    }

    public int Count => _items.Length;

    public T this[int index] => _items[index].Value;

    public static DeferredItemList<T> Create<TSource>(IEnumerable<TSource> source, Func<TSource, T> selector)
    {
        ArgumentNullException.ThrowIfNull(argument: source);
        ArgumentNullException.ThrowIfNull(argument: selector);
        return new DeferredItemList<T>(
            items: source.Select(selector: item => new Lazy<T>(valueFactory: () => selector(arg: item))).ToArray());
    }

    public IEnumerator<T> GetEnumerator()
    {
        foreach (var item in _items)
        {
            yield return item.Value;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
