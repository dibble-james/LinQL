namespace LinQL.StrawberryShake;

/// <summary>
/// Maps the values of an observable sequence.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="MappedObservable{TSource, TResult}"/> class.
/// </remarks>
internal sealed class MappedObservable<TSource, TResult>(IObservable<TSource> source, Func<TSource, TResult> selector) : IObservable<TResult>
{
    private readonly IObservable<TSource> source = source;
    private readonly Func<TSource, TResult> selector = selector;

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
