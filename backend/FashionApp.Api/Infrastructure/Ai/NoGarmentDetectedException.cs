namespace FashionApp.Api.Infrastructure.Ai;

/// <summary>
/// Raised when the model explicitly answers <c>{"error":"no_garment"}</c>: the
/// photo does not contain a garment. It is not a transport failure, so the
/// endpoints must not turn it into a zero-confidence "Prenda".
/// </summary>
public sealed class NoGarmentDetectedException : Exception
{
    public NoGarmentDetectedException()
        : base("La foto no contiene una prenda detectable.")
    {
    }
}
