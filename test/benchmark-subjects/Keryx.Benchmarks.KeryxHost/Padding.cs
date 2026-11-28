// Dispatch padding, compiled only when the build property KeryxDispatchPadding is true (set it as an environment variable so BenchmarkDotNet's
// own build sees it too): 32 more request, stream request and notification types, each with a static handler, so this host has more than
// HostEmitter.DispatchThreshold of each and its generic Send, Stream and Publish dispatch through the FrozenDictionary index and a jump
// table instead of the linear compares. The comparison benchmarks' Keryx_Generic rows then measure the above-threshold path, for example:
//   KeryxDispatchPadding=true dotnet run -c Release -f net10.0 -- --filter '*Keryx_Generic*'
#if KERYX_DISPATCH_PADDING
namespace Keryx.Benchmarks.KeryxHost;

public sealed record PadRequest00(int Value) : IRequest<int>;
public sealed record PadRequest01(int Value) : IRequest<int>;
public sealed record PadRequest02(int Value) : IRequest<int>;
public sealed record PadRequest03(int Value) : IRequest<int>;
public sealed record PadRequest04(int Value) : IRequest<int>;
public sealed record PadRequest05(int Value) : IRequest<int>;
public sealed record PadRequest06(int Value) : IRequest<int>;
public sealed record PadRequest07(int Value) : IRequest<int>;
public sealed record PadRequest08(int Value) : IRequest<int>;
public sealed record PadRequest09(int Value) : IRequest<int>;
public sealed record PadRequest10(int Value) : IRequest<int>;
public sealed record PadRequest11(int Value) : IRequest<int>;
public sealed record PadRequest12(int Value) : IRequest<int>;
public sealed record PadRequest13(int Value) : IRequest<int>;
public sealed record PadRequest14(int Value) : IRequest<int>;
public sealed record PadRequest15(int Value) : IRequest<int>;
public sealed record PadRequest16(int Value) : IRequest<int>;
public sealed record PadRequest17(int Value) : IRequest<int>;
public sealed record PadRequest18(int Value) : IRequest<int>;
public sealed record PadRequest19(int Value) : IRequest<int>;
public sealed record PadRequest20(int Value) : IRequest<int>;
public sealed record PadRequest21(int Value) : IRequest<int>;
public sealed record PadRequest22(int Value) : IRequest<int>;
public sealed record PadRequest23(int Value) : IRequest<int>;
public sealed record PadRequest24(int Value) : IRequest<int>;
public sealed record PadRequest25(int Value) : IRequest<int>;
public sealed record PadRequest26(int Value) : IRequest<int>;
public sealed record PadRequest27(int Value) : IRequest<int>;
public sealed record PadRequest28(int Value) : IRequest<int>;
public sealed record PadRequest29(int Value) : IRequest<int>;
public sealed record PadRequest30(int Value) : IRequest<int>;
public sealed record PadRequest31(int Value) : IRequest<int>;

public sealed record PadStream00(int Count) : IStreamRequest<int>;
public sealed record PadStream01(int Count) : IStreamRequest<int>;
public sealed record PadStream02(int Count) : IStreamRequest<int>;
public sealed record PadStream03(int Count) : IStreamRequest<int>;
public sealed record PadStream04(int Count) : IStreamRequest<int>;
public sealed record PadStream05(int Count) : IStreamRequest<int>;
public sealed record PadStream06(int Count) : IStreamRequest<int>;
public sealed record PadStream07(int Count) : IStreamRequest<int>;
public sealed record PadStream08(int Count) : IStreamRequest<int>;
public sealed record PadStream09(int Count) : IStreamRequest<int>;
public sealed record PadStream10(int Count) : IStreamRequest<int>;
public sealed record PadStream11(int Count) : IStreamRequest<int>;
public sealed record PadStream12(int Count) : IStreamRequest<int>;
public sealed record PadStream13(int Count) : IStreamRequest<int>;
public sealed record PadStream14(int Count) : IStreamRequest<int>;
public sealed record PadStream15(int Count) : IStreamRequest<int>;
public sealed record PadStream16(int Count) : IStreamRequest<int>;
public sealed record PadStream17(int Count) : IStreamRequest<int>;
public sealed record PadStream18(int Count) : IStreamRequest<int>;
public sealed record PadStream19(int Count) : IStreamRequest<int>;
public sealed record PadStream20(int Count) : IStreamRequest<int>;
public sealed record PadStream21(int Count) : IStreamRequest<int>;
public sealed record PadStream22(int Count) : IStreamRequest<int>;
public sealed record PadStream23(int Count) : IStreamRequest<int>;
public sealed record PadStream24(int Count) : IStreamRequest<int>;
public sealed record PadStream25(int Count) : IStreamRequest<int>;
public sealed record PadStream26(int Count) : IStreamRequest<int>;
public sealed record PadStream27(int Count) : IStreamRequest<int>;
public sealed record PadStream28(int Count) : IStreamRequest<int>;
public sealed record PadStream29(int Count) : IStreamRequest<int>;
public sealed record PadStream30(int Count) : IStreamRequest<int>;
public sealed record PadStream31(int Count) : IStreamRequest<int>;

public sealed record PadNotification00(int Value) : INotification;
public sealed record PadNotification01(int Value) : INotification;
public sealed record PadNotification02(int Value) : INotification;
public sealed record PadNotification03(int Value) : INotification;
public sealed record PadNotification04(int Value) : INotification;
public sealed record PadNotification05(int Value) : INotification;
public sealed record PadNotification06(int Value) : INotification;
public sealed record PadNotification07(int Value) : INotification;
public sealed record PadNotification08(int Value) : INotification;
public sealed record PadNotification09(int Value) : INotification;
public sealed record PadNotification10(int Value) : INotification;
public sealed record PadNotification11(int Value) : INotification;
public sealed record PadNotification12(int Value) : INotification;
public sealed record PadNotification13(int Value) : INotification;
public sealed record PadNotification14(int Value) : INotification;
public sealed record PadNotification15(int Value) : INotification;
public sealed record PadNotification16(int Value) : INotification;
public sealed record PadNotification17(int Value) : INotification;
public sealed record PadNotification18(int Value) : INotification;
public sealed record PadNotification19(int Value) : INotification;
public sealed record PadNotification20(int Value) : INotification;
public sealed record PadNotification21(int Value) : INotification;
public sealed record PadNotification22(int Value) : INotification;
public sealed record PadNotification23(int Value) : INotification;
public sealed record PadNotification24(int Value) : INotification;
public sealed record PadNotification25(int Value) : INotification;
public sealed record PadNotification26(int Value) : INotification;
public sealed record PadNotification27(int Value) : INotification;
public sealed record PadNotification28(int Value) : INotification;
public sealed record PadNotification29(int Value) : INotification;
public sealed record PadNotification30(int Value) : INotification;
public sealed record PadNotification31(int Value) : INotification;

public static partial class PadRequest00Handler
{
    private static ValueTask<int> Handle(PadRequest00 request) => new(request.Value);
}

public static partial class PadRequest01Handler
{
    private static ValueTask<int> Handle(PadRequest01 request) => new(request.Value);
}

public static partial class PadRequest02Handler
{
    private static ValueTask<int> Handle(PadRequest02 request) => new(request.Value);
}

public static partial class PadRequest03Handler
{
    private static ValueTask<int> Handle(PadRequest03 request) => new(request.Value);
}

public static partial class PadRequest04Handler
{
    private static ValueTask<int> Handle(PadRequest04 request) => new(request.Value);
}

public static partial class PadRequest05Handler
{
    private static ValueTask<int> Handle(PadRequest05 request) => new(request.Value);
}

public static partial class PadRequest06Handler
{
    private static ValueTask<int> Handle(PadRequest06 request) => new(request.Value);
}

public static partial class PadRequest07Handler
{
    private static ValueTask<int> Handle(PadRequest07 request) => new(request.Value);
}

public static partial class PadRequest08Handler
{
    private static ValueTask<int> Handle(PadRequest08 request) => new(request.Value);
}

public static partial class PadRequest09Handler
{
    private static ValueTask<int> Handle(PadRequest09 request) => new(request.Value);
}

public static partial class PadRequest10Handler
{
    private static ValueTask<int> Handle(PadRequest10 request) => new(request.Value);
}

public static partial class PadRequest11Handler
{
    private static ValueTask<int> Handle(PadRequest11 request) => new(request.Value);
}

public static partial class PadRequest12Handler
{
    private static ValueTask<int> Handle(PadRequest12 request) => new(request.Value);
}

public static partial class PadRequest13Handler
{
    private static ValueTask<int> Handle(PadRequest13 request) => new(request.Value);
}

public static partial class PadRequest14Handler
{
    private static ValueTask<int> Handle(PadRequest14 request) => new(request.Value);
}

public static partial class PadRequest15Handler
{
    private static ValueTask<int> Handle(PadRequest15 request) => new(request.Value);
}

public static partial class PadRequest16Handler
{
    private static ValueTask<int> Handle(PadRequest16 request) => new(request.Value);
}

public static partial class PadRequest17Handler
{
    private static ValueTask<int> Handle(PadRequest17 request) => new(request.Value);
}

public static partial class PadRequest18Handler
{
    private static ValueTask<int> Handle(PadRequest18 request) => new(request.Value);
}

public static partial class PadRequest19Handler
{
    private static ValueTask<int> Handle(PadRequest19 request) => new(request.Value);
}

public static partial class PadRequest20Handler
{
    private static ValueTask<int> Handle(PadRequest20 request) => new(request.Value);
}

public static partial class PadRequest21Handler
{
    private static ValueTask<int> Handle(PadRequest21 request) => new(request.Value);
}

public static partial class PadRequest22Handler
{
    private static ValueTask<int> Handle(PadRequest22 request) => new(request.Value);
}

public static partial class PadRequest23Handler
{
    private static ValueTask<int> Handle(PadRequest23 request) => new(request.Value);
}

public static partial class PadRequest24Handler
{
    private static ValueTask<int> Handle(PadRequest24 request) => new(request.Value);
}

public static partial class PadRequest25Handler
{
    private static ValueTask<int> Handle(PadRequest25 request) => new(request.Value);
}

public static partial class PadRequest26Handler
{
    private static ValueTask<int> Handle(PadRequest26 request) => new(request.Value);
}

public static partial class PadRequest27Handler
{
    private static ValueTask<int> Handle(PadRequest27 request) => new(request.Value);
}

public static partial class PadRequest28Handler
{
    private static ValueTask<int> Handle(PadRequest28 request) => new(request.Value);
}

public static partial class PadRequest29Handler
{
    private static ValueTask<int> Handle(PadRequest29 request) => new(request.Value);
}

public static partial class PadRequest30Handler
{
    private static ValueTask<int> Handle(PadRequest30 request) => new(request.Value);
}

public static partial class PadRequest31Handler
{
    private static ValueTask<int> Handle(PadRequest31 request) => new(request.Value);
}

public static partial class PadStream00Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream00 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream01Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream01 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream02Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream02 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream03Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream03 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream04Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream04 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream05Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream05 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream06Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream06 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream07Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream07 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream08Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream08 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream09Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream09 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream10Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream10 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream11Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream11 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream12Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream12 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream13Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream13 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream14Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream14 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream15Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream15 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream16Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream16 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream17Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream17 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream18Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream18 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream19Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream19 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream20Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream20 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream21Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream21 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream22Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream22 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream23Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream23 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream24Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream24 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream25Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream25 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream26Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream26 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream27Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream27 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream28Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream28 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream29Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream29 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream30Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream30 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadStream31Handler
{
    private static IAsyncEnumerable<int> Handle(PadStream31 request) => Common.Numbers.Range(request.Count);
}

public static partial class PadNotification00Handler
{
    private static ValueTask Handle(PadNotification00 notification) => default;
}

public static partial class PadNotification01Handler
{
    private static ValueTask Handle(PadNotification01 notification) => default;
}

public static partial class PadNotification02Handler
{
    private static ValueTask Handle(PadNotification02 notification) => default;
}

public static partial class PadNotification03Handler
{
    private static ValueTask Handle(PadNotification03 notification) => default;
}

public static partial class PadNotification04Handler
{
    private static ValueTask Handle(PadNotification04 notification) => default;
}

public static partial class PadNotification05Handler
{
    private static ValueTask Handle(PadNotification05 notification) => default;
}

public static partial class PadNotification06Handler
{
    private static ValueTask Handle(PadNotification06 notification) => default;
}

public static partial class PadNotification07Handler
{
    private static ValueTask Handle(PadNotification07 notification) => default;
}

public static partial class PadNotification08Handler
{
    private static ValueTask Handle(PadNotification08 notification) => default;
}

public static partial class PadNotification09Handler
{
    private static ValueTask Handle(PadNotification09 notification) => default;
}

public static partial class PadNotification10Handler
{
    private static ValueTask Handle(PadNotification10 notification) => default;
}

public static partial class PadNotification11Handler
{
    private static ValueTask Handle(PadNotification11 notification) => default;
}

public static partial class PadNotification12Handler
{
    private static ValueTask Handle(PadNotification12 notification) => default;
}

public static partial class PadNotification13Handler
{
    private static ValueTask Handle(PadNotification13 notification) => default;
}

public static partial class PadNotification14Handler
{
    private static ValueTask Handle(PadNotification14 notification) => default;
}

public static partial class PadNotification15Handler
{
    private static ValueTask Handle(PadNotification15 notification) => default;
}

public static partial class PadNotification16Handler
{
    private static ValueTask Handle(PadNotification16 notification) => default;
}

public static partial class PadNotification17Handler
{
    private static ValueTask Handle(PadNotification17 notification) => default;
}

public static partial class PadNotification18Handler
{
    private static ValueTask Handle(PadNotification18 notification) => default;
}

public static partial class PadNotification19Handler
{
    private static ValueTask Handle(PadNotification19 notification) => default;
}

public static partial class PadNotification20Handler
{
    private static ValueTask Handle(PadNotification20 notification) => default;
}

public static partial class PadNotification21Handler
{
    private static ValueTask Handle(PadNotification21 notification) => default;
}

public static partial class PadNotification22Handler
{
    private static ValueTask Handle(PadNotification22 notification) => default;
}

public static partial class PadNotification23Handler
{
    private static ValueTask Handle(PadNotification23 notification) => default;
}

public static partial class PadNotification24Handler
{
    private static ValueTask Handle(PadNotification24 notification) => default;
}

public static partial class PadNotification25Handler
{
    private static ValueTask Handle(PadNotification25 notification) => default;
}

public static partial class PadNotification26Handler
{
    private static ValueTask Handle(PadNotification26 notification) => default;
}

public static partial class PadNotification27Handler
{
    private static ValueTask Handle(PadNotification27 notification) => default;
}

public static partial class PadNotification28Handler
{
    private static ValueTask Handle(PadNotification28 notification) => default;
}

public static partial class PadNotification29Handler
{
    private static ValueTask Handle(PadNotification29 notification) => default;
}

public static partial class PadNotification30Handler
{
    private static ValueTask Handle(PadNotification30 notification) => default;
}

public static partial class PadNotification31Handler
{
    private static ValueTask Handle(PadNotification31 notification) => default;
}

#endif
