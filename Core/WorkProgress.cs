namespace AxoClient.Core;

public sealed class WorkProgress(IProgress<string> text, IProgress<double> fraction, CancellationToken cancel)
{
    public IProgress<string> Text { get; } = text;
    public IProgress<double> Fraction { get; } = fraction;
    public CancellationToken Cancel { get; } = cancel;

    public static WorkProgress Silent => For(new Progress<string>());

    public static WorkProgress For(IProgress<string> status) =>
        new(status, new Progress<double>(), CancellationToken.None);
}
