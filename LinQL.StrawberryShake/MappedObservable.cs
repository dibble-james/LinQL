namespace LinQL.StrawberryShake;

/// <summary>
/// Maps the values of an observable sequence.
/// </summary>
/// <typeparam name="TSource">The source value type.</typeparam>
/// <typeparam name="TResult">The mapped value type.</typeparam>
internal sealed class MappedObservable<TSource, TResult> : IObservable<TResult>
{
    private readonly IObservable<TSource> source;
    private readonly Func<TSource, TResult> selector;

    /// <summary>
    /// Initializes a new instance of the <see cref="MappedObservable{TSource, TResult}"/> class.
    /// </summary>
    /// <param name="source">The sequence to map.</param>
    /// <param name="selector">The mapping.</param>
    public MappedObservable(IObservable<TSource> source, Func<TSource, TResult> selector)
    {
        this.source = source;
        this.selector = selector;
    }

    /// <inheritdoc />
    public IDisposable Subscribe(IObserver<TResult> observer)
        => this.source.Subscribe(new MappedObserver(observer, this.selector));

    private sealed class MappedObserver(IObserver<TResult> observer, Func<TSource, TResult> selector) : IObserver<TSource>
    {
        public void OnCompleted() => observer.OnCompleted();

        public void OnError(Exception error) => observer.OnError(error);

        public void OnNext(TSource value) => observer.OnNext(selector(value));
    }
}
