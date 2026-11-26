// Hand-written in the shape the host generator emits for generic Send (HostEmitter), used to set HostEmitter.DispatchThreshold
// (open-questions OQ-15). The repetitive parts are written out on purpose: the measured shape must be the generated one.
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;

namespace Keryx.Benchmarks.Host;

/// <summary>
///     Generic <c>Send</c> dispatch: a linear chain of exact-type compares against a <c>FrozenDictionary&lt;Type, int&gt;</c> lookup followed by
///     a <c>switch</c> (ADR 0001, Generic entry points). <see cref="Count" /> is the number of request types the host dispatches; the request
///     sent is the last one in the chain (the worst case for the linear chain) or the middle one (its average). Each arm calls a
///     non-inlined per-request method that reinterprets the result with <c>Unsafe.As</c>, as the generated code does (OQ-15).
/// </summary>
[MemoryDiagnoser]
public class DispatchBenchmarks
{
    private static readonly IRequest<int>[] Requests =
    [
        new R00(0),
        new R01(1),
        new R02(2),
        new R03(3),
        new R04(4),
        new R05(5),
        new R06(6),
        new R07(7),
        new R08(8),
        new R09(9),
        new R10(10),
        new R11(11),
        new R12(12),
        new R13(13),
        new R14(14),
        new R15(15),
        new R16(16),
        new R17(17),
        new R18(18),
        new R19(19),
        new R20(20),
        new R21(21),
        new R22(22),
        new R23(23),
        new R24(24),
        new R25(25),
        new R26(26),
        new R27(27),
        new R28(28),
        new R29(29),
        new R30(30),
        new R31(31),
        new R32(32),
        new R33(33),
        new R34(34),
        new R35(35),
        new R36(36),
        new R37(37),
        new R38(38),
        new R39(39),
        new R40(40),
        new R41(41),
        new R42(42),
        new R43(43),
        new R44(44),
        new R45(45),
        new R46(46),
        new R47(47),
        new R48(48),
        new R49(49),
        new R50(50),
        new R51(51),
        new R52(52),
        new R53(53),
        new R54(54),
        new R55(55),
        new R56(56),
        new R57(57),
        new R58(58),
        new R59(59),
        new R60(60),
        new R61(61),
        new R62(62),
        new R63(63),
    ];

    private static FrozenDictionary<Type, int> _index = null!;
    private IRequest<int> _last = null!;
    private IRequest<int> _middle = null!;

    [Params(8, 16, 24, 32, 48, 64)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _index = Requests.Take(Count).Select(static (request, index) => KeyValuePair.Create(request.GetType(), index)).ToFrozenDictionary();
        _last = Requests[Count - 1];
        _middle = Requests[Count / 2];
    }

    [Benchmark(Baseline = true)]
    public int LinearLast() => Completed.Result(Linear(_last));

    [Benchmark]
    public int LinearMiddle() => Completed.Result(Linear(_middle));

    [Benchmark]
    public int FrozenLast() => Completed.Result(Frozen(_last));

    [Benchmark]
    public int FrozenMiddle() => Completed.Result(Frozen(_middle));

    // The chain is the generated shape being measured; folding it into a conditional expression would change what is benchmarked.
#pragma warning disable IDE0046
    private static ValueTask<TResponse> Linear<TResponse>(IRequest<TResponse> request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var requestType = request.GetType();
        if (requestType == typeof(R00))
        {
            return Dispatch00(request);
        }

        if (requestType == typeof(R01))
        {
            return Dispatch01(request);
        }

        if (requestType == typeof(R02))
        {
            return Dispatch02(request);
        }

        if (requestType == typeof(R03))
        {
            return Dispatch03(request);
        }

        if (requestType == typeof(R04))
        {
            return Dispatch04(request);
        }

        if (requestType == typeof(R05))
        {
            return Dispatch05(request);
        }

        if (requestType == typeof(R06))
        {
            return Dispatch06(request);
        }

        if (requestType == typeof(R07))
        {
            return Dispatch07(request);
        }

        if (requestType == typeof(R08))
        {
            return Dispatch08(request);
        }

        if (requestType == typeof(R09))
        {
            return Dispatch09(request);
        }

        if (requestType == typeof(R10))
        {
            return Dispatch10(request);
        }

        if (requestType == typeof(R11))
        {
            return Dispatch11(request);
        }

        if (requestType == typeof(R12))
        {
            return Dispatch12(request);
        }

        if (requestType == typeof(R13))
        {
            return Dispatch13(request);
        }

        if (requestType == typeof(R14))
        {
            return Dispatch14(request);
        }

        if (requestType == typeof(R15))
        {
            return Dispatch15(request);
        }

        if (requestType == typeof(R16))
        {
            return Dispatch16(request);
        }

        if (requestType == typeof(R17))
        {
            return Dispatch17(request);
        }

        if (requestType == typeof(R18))
        {
            return Dispatch18(request);
        }

        if (requestType == typeof(R19))
        {
            return Dispatch19(request);
        }

        if (requestType == typeof(R20))
        {
            return Dispatch20(request);
        }

        if (requestType == typeof(R21))
        {
            return Dispatch21(request);
        }

        if (requestType == typeof(R22))
        {
            return Dispatch22(request);
        }

        if (requestType == typeof(R23))
        {
            return Dispatch23(request);
        }

        if (requestType == typeof(R24))
        {
            return Dispatch24(request);
        }

        if (requestType == typeof(R25))
        {
            return Dispatch25(request);
        }

        if (requestType == typeof(R26))
        {
            return Dispatch26(request);
        }

        if (requestType == typeof(R27))
        {
            return Dispatch27(request);
        }

        if (requestType == typeof(R28))
        {
            return Dispatch28(request);
        }

        if (requestType == typeof(R29))
        {
            return Dispatch29(request);
        }

        if (requestType == typeof(R30))
        {
            return Dispatch30(request);
        }

        if (requestType == typeof(R31))
        {
            return Dispatch31(request);
        }

        if (requestType == typeof(R32))
        {
            return Dispatch32(request);
        }

        if (requestType == typeof(R33))
        {
            return Dispatch33(request);
        }

        if (requestType == typeof(R34))
        {
            return Dispatch34(request);
        }

        if (requestType == typeof(R35))
        {
            return Dispatch35(request);
        }

        if (requestType == typeof(R36))
        {
            return Dispatch36(request);
        }

        if (requestType == typeof(R37))
        {
            return Dispatch37(request);
        }

        if (requestType == typeof(R38))
        {
            return Dispatch38(request);
        }

        if (requestType == typeof(R39))
        {
            return Dispatch39(request);
        }

        if (requestType == typeof(R40))
        {
            return Dispatch40(request);
        }

        if (requestType == typeof(R41))
        {
            return Dispatch41(request);
        }

        if (requestType == typeof(R42))
        {
            return Dispatch42(request);
        }

        if (requestType == typeof(R43))
        {
            return Dispatch43(request);
        }

        if (requestType == typeof(R44))
        {
            return Dispatch44(request);
        }

        if (requestType == typeof(R45))
        {
            return Dispatch45(request);
        }

        if (requestType == typeof(R46))
        {
            return Dispatch46(request);
        }

        if (requestType == typeof(R47))
        {
            return Dispatch47(request);
        }

        if (requestType == typeof(R48))
        {
            return Dispatch48(request);
        }

        if (requestType == typeof(R49))
        {
            return Dispatch49(request);
        }

        if (requestType == typeof(R50))
        {
            return Dispatch50(request);
        }

        if (requestType == typeof(R51))
        {
            return Dispatch51(request);
        }

        if (requestType == typeof(R52))
        {
            return Dispatch52(request);
        }

        if (requestType == typeof(R53))
        {
            return Dispatch53(request);
        }

        if (requestType == typeof(R54))
        {
            return Dispatch54(request);
        }

        if (requestType == typeof(R55))
        {
            return Dispatch55(request);
        }

        if (requestType == typeof(R56))
        {
            return Dispatch56(request);
        }

        if (requestType == typeof(R57))
        {
            return Dispatch57(request);
        }

        if (requestType == typeof(R58))
        {
            return Dispatch58(request);
        }

        if (requestType == typeof(R59))
        {
            return Dispatch59(request);
        }

        if (requestType == typeof(R60))
        {
            return Dispatch60(request);
        }

        if (requestType == typeof(R61))
        {
            return Dispatch61(request);
        }

        if (requestType == typeof(R62))
        {
            return Dispatch62(request);
        }

        if (requestType == typeof(R63))
        {
            return Dispatch63(request);
        }

        throw new KeryxHandlerNotFoundException(requestType);
    }
#pragma warning restore IDE0046

    private static ValueTask<TResponse> Frozen<TResponse>(IRequest<TResponse> request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var requestType = request.GetType();
        return RequestIndex(requestType) switch
        {
            0 => Dispatch00(request),
            1 => Dispatch01(request),
            2 => Dispatch02(request),
            3 => Dispatch03(request),
            4 => Dispatch04(request),
            5 => Dispatch05(request),
            6 => Dispatch06(request),
            7 => Dispatch07(request),
            8 => Dispatch08(request),
            9 => Dispatch09(request),
            10 => Dispatch10(request),
            11 => Dispatch11(request),
            12 => Dispatch12(request),
            13 => Dispatch13(request),
            14 => Dispatch14(request),
            15 => Dispatch15(request),
            16 => Dispatch16(request),
            17 => Dispatch17(request),
            18 => Dispatch18(request),
            19 => Dispatch19(request),
            20 => Dispatch20(request),
            21 => Dispatch21(request),
            22 => Dispatch22(request),
            23 => Dispatch23(request),
            24 => Dispatch24(request),
            25 => Dispatch25(request),
            26 => Dispatch26(request),
            27 => Dispatch27(request),
            28 => Dispatch28(request),
            29 => Dispatch29(request),
            30 => Dispatch30(request),
            31 => Dispatch31(request),
            32 => Dispatch32(request),
            33 => Dispatch33(request),
            34 => Dispatch34(request),
            35 => Dispatch35(request),
            36 => Dispatch36(request),
            37 => Dispatch37(request),
            38 => Dispatch38(request),
            39 => Dispatch39(request),
            40 => Dispatch40(request),
            41 => Dispatch41(request),
            42 => Dispatch42(request),
            43 => Dispatch43(request),
            44 => Dispatch44(request),
            45 => Dispatch45(request),
            46 => Dispatch46(request),
            47 => Dispatch47(request),
            48 => Dispatch48(request),
            49 => Dispatch49(request),
            50 => Dispatch50(request),
            51 => Dispatch51(request),
            52 => Dispatch52(request),
            53 => Dispatch53(request),
            54 => Dispatch54(request),
            55 => Dispatch55(request),
            56 => Dispatch56(request),
            57 => Dispatch57(request),
            58 => Dispatch58(request),
            59 => Dispatch59(request),
            60 => Dispatch60(request),
            61 => Dispatch61(request),
            62 => Dispatch62(request),
            63 => Dispatch63(request),
            _ => throw new KeryxHandlerNotFoundException(requestType),
        };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RequestIndex(Type requestType) => _index.TryGetValue(requestType, out var index) ? index : -1;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch00<TResponse>(IRequest<TResponse> request)
    {
        var response = Send00(Unsafe.As<R00>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send00(R00 request) => new(request.Value + 0);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch01<TResponse>(IRequest<TResponse> request)
    {
        var response = Send01(Unsafe.As<R01>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send01(R01 request) => new(request.Value + 1);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch02<TResponse>(IRequest<TResponse> request)
    {
        var response = Send02(Unsafe.As<R02>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send02(R02 request) => new(request.Value + 2);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch03<TResponse>(IRequest<TResponse> request)
    {
        var response = Send03(Unsafe.As<R03>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send03(R03 request) => new(request.Value + 3);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch04<TResponse>(IRequest<TResponse> request)
    {
        var response = Send04(Unsafe.As<R04>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send04(R04 request) => new(request.Value + 4);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch05<TResponse>(IRequest<TResponse> request)
    {
        var response = Send05(Unsafe.As<R05>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send05(R05 request) => new(request.Value + 5);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch06<TResponse>(IRequest<TResponse> request)
    {
        var response = Send06(Unsafe.As<R06>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send06(R06 request) => new(request.Value + 6);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch07<TResponse>(IRequest<TResponse> request)
    {
        var response = Send07(Unsafe.As<R07>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send07(R07 request) => new(request.Value + 7);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch08<TResponse>(IRequest<TResponse> request)
    {
        var response = Send08(Unsafe.As<R08>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send08(R08 request) => new(request.Value + 8);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch09<TResponse>(IRequest<TResponse> request)
    {
        var response = Send09(Unsafe.As<R09>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send09(R09 request) => new(request.Value + 9);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch10<TResponse>(IRequest<TResponse> request)
    {
        var response = Send10(Unsafe.As<R10>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send10(R10 request) => new(request.Value + 10);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch11<TResponse>(IRequest<TResponse> request)
    {
        var response = Send11(Unsafe.As<R11>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send11(R11 request) => new(request.Value + 11);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch12<TResponse>(IRequest<TResponse> request)
    {
        var response = Send12(Unsafe.As<R12>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send12(R12 request) => new(request.Value + 12);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch13<TResponse>(IRequest<TResponse> request)
    {
        var response = Send13(Unsafe.As<R13>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send13(R13 request) => new(request.Value + 13);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch14<TResponse>(IRequest<TResponse> request)
    {
        var response = Send14(Unsafe.As<R14>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send14(R14 request) => new(request.Value + 14);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch15<TResponse>(IRequest<TResponse> request)
    {
        var response = Send15(Unsafe.As<R15>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send15(R15 request) => new(request.Value + 15);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch16<TResponse>(IRequest<TResponse> request)
    {
        var response = Send16(Unsafe.As<R16>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send16(R16 request) => new(request.Value + 16);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch17<TResponse>(IRequest<TResponse> request)
    {
        var response = Send17(Unsafe.As<R17>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send17(R17 request) => new(request.Value + 17);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch18<TResponse>(IRequest<TResponse> request)
    {
        var response = Send18(Unsafe.As<R18>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send18(R18 request) => new(request.Value + 18);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch19<TResponse>(IRequest<TResponse> request)
    {
        var response = Send19(Unsafe.As<R19>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send19(R19 request) => new(request.Value + 19);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch20<TResponse>(IRequest<TResponse> request)
    {
        var response = Send20(Unsafe.As<R20>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send20(R20 request) => new(request.Value + 20);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch21<TResponse>(IRequest<TResponse> request)
    {
        var response = Send21(Unsafe.As<R21>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send21(R21 request) => new(request.Value + 21);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch22<TResponse>(IRequest<TResponse> request)
    {
        var response = Send22(Unsafe.As<R22>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send22(R22 request) => new(request.Value + 22);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch23<TResponse>(IRequest<TResponse> request)
    {
        var response = Send23(Unsafe.As<R23>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send23(R23 request) => new(request.Value + 23);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch24<TResponse>(IRequest<TResponse> request)
    {
        var response = Send24(Unsafe.As<R24>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send24(R24 request) => new(request.Value + 24);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch25<TResponse>(IRequest<TResponse> request)
    {
        var response = Send25(Unsafe.As<R25>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send25(R25 request) => new(request.Value + 25);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch26<TResponse>(IRequest<TResponse> request)
    {
        var response = Send26(Unsafe.As<R26>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send26(R26 request) => new(request.Value + 26);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch27<TResponse>(IRequest<TResponse> request)
    {
        var response = Send27(Unsafe.As<R27>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send27(R27 request) => new(request.Value + 27);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch28<TResponse>(IRequest<TResponse> request)
    {
        var response = Send28(Unsafe.As<R28>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send28(R28 request) => new(request.Value + 28);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch29<TResponse>(IRequest<TResponse> request)
    {
        var response = Send29(Unsafe.As<R29>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send29(R29 request) => new(request.Value + 29);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch30<TResponse>(IRequest<TResponse> request)
    {
        var response = Send30(Unsafe.As<R30>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send30(R30 request) => new(request.Value + 30);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch31<TResponse>(IRequest<TResponse> request)
    {
        var response = Send31(Unsafe.As<R31>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send31(R31 request) => new(request.Value + 31);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch32<TResponse>(IRequest<TResponse> request)
    {
        var response = Send32(Unsafe.As<R32>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send32(R32 request) => new(request.Value + 32);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch33<TResponse>(IRequest<TResponse> request)
    {
        var response = Send33(Unsafe.As<R33>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send33(R33 request) => new(request.Value + 33);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch34<TResponse>(IRequest<TResponse> request)
    {
        var response = Send34(Unsafe.As<R34>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send34(R34 request) => new(request.Value + 34);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch35<TResponse>(IRequest<TResponse> request)
    {
        var response = Send35(Unsafe.As<R35>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send35(R35 request) => new(request.Value + 35);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch36<TResponse>(IRequest<TResponse> request)
    {
        var response = Send36(Unsafe.As<R36>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send36(R36 request) => new(request.Value + 36);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch37<TResponse>(IRequest<TResponse> request)
    {
        var response = Send37(Unsafe.As<R37>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send37(R37 request) => new(request.Value + 37);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch38<TResponse>(IRequest<TResponse> request)
    {
        var response = Send38(Unsafe.As<R38>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send38(R38 request) => new(request.Value + 38);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch39<TResponse>(IRequest<TResponse> request)
    {
        var response = Send39(Unsafe.As<R39>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send39(R39 request) => new(request.Value + 39);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch40<TResponse>(IRequest<TResponse> request)
    {
        var response = Send40(Unsafe.As<R40>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send40(R40 request) => new(request.Value + 40);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch41<TResponse>(IRequest<TResponse> request)
    {
        var response = Send41(Unsafe.As<R41>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send41(R41 request) => new(request.Value + 41);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch42<TResponse>(IRequest<TResponse> request)
    {
        var response = Send42(Unsafe.As<R42>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send42(R42 request) => new(request.Value + 42);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch43<TResponse>(IRequest<TResponse> request)
    {
        var response = Send43(Unsafe.As<R43>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send43(R43 request) => new(request.Value + 43);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch44<TResponse>(IRequest<TResponse> request)
    {
        var response = Send44(Unsafe.As<R44>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send44(R44 request) => new(request.Value + 44);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch45<TResponse>(IRequest<TResponse> request)
    {
        var response = Send45(Unsafe.As<R45>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send45(R45 request) => new(request.Value + 45);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch46<TResponse>(IRequest<TResponse> request)
    {
        var response = Send46(Unsafe.As<R46>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send46(R46 request) => new(request.Value + 46);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch47<TResponse>(IRequest<TResponse> request)
    {
        var response = Send47(Unsafe.As<R47>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send47(R47 request) => new(request.Value + 47);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch48<TResponse>(IRequest<TResponse> request)
    {
        var response = Send48(Unsafe.As<R48>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send48(R48 request) => new(request.Value + 48);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch49<TResponse>(IRequest<TResponse> request)
    {
        var response = Send49(Unsafe.As<R49>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send49(R49 request) => new(request.Value + 49);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch50<TResponse>(IRequest<TResponse> request)
    {
        var response = Send50(Unsafe.As<R50>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send50(R50 request) => new(request.Value + 50);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch51<TResponse>(IRequest<TResponse> request)
    {
        var response = Send51(Unsafe.As<R51>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send51(R51 request) => new(request.Value + 51);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch52<TResponse>(IRequest<TResponse> request)
    {
        var response = Send52(Unsafe.As<R52>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send52(R52 request) => new(request.Value + 52);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch53<TResponse>(IRequest<TResponse> request)
    {
        var response = Send53(Unsafe.As<R53>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send53(R53 request) => new(request.Value + 53);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch54<TResponse>(IRequest<TResponse> request)
    {
        var response = Send54(Unsafe.As<R54>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send54(R54 request) => new(request.Value + 54);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch55<TResponse>(IRequest<TResponse> request)
    {
        var response = Send55(Unsafe.As<R55>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send55(R55 request) => new(request.Value + 55);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch56<TResponse>(IRequest<TResponse> request)
    {
        var response = Send56(Unsafe.As<R56>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send56(R56 request) => new(request.Value + 56);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch57<TResponse>(IRequest<TResponse> request)
    {
        var response = Send57(Unsafe.As<R57>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send57(R57 request) => new(request.Value + 57);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch58<TResponse>(IRequest<TResponse> request)
    {
        var response = Send58(Unsafe.As<R58>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send58(R58 request) => new(request.Value + 58);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch59<TResponse>(IRequest<TResponse> request)
    {
        var response = Send59(Unsafe.As<R59>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send59(R59 request) => new(request.Value + 59);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch60<TResponse>(IRequest<TResponse> request)
    {
        var response = Send60(Unsafe.As<R60>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send60(R60 request) => new(request.Value + 60);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch61<TResponse>(IRequest<TResponse> request)
    {
        var response = Send61(Unsafe.As<R61>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send61(R61 request) => new(request.Value + 61);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch62<TResponse>(IRequest<TResponse> request)
    {
        var response = Send62(Unsafe.As<R62>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send62(R62 request) => new(request.Value + 62);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<TResponse> Dispatch63<TResponse>(IRequest<TResponse> request)
    {
        var response = Send63(Unsafe.As<R63>(request));
        return Unsafe.As<ValueTask<int>, ValueTask<TResponse>>(ref response);
    }

    private static ValueTask<int> Send63(R63 request) => new(request.Value + 63);
}

internal sealed record R00(int Value) : IRequest<int>;

internal sealed record R01(int Value) : IRequest<int>;

internal sealed record R02(int Value) : IRequest<int>;

internal sealed record R03(int Value) : IRequest<int>;

internal sealed record R04(int Value) : IRequest<int>;

internal sealed record R05(int Value) : IRequest<int>;

internal sealed record R06(int Value) : IRequest<int>;

internal sealed record R07(int Value) : IRequest<int>;

internal sealed record R08(int Value) : IRequest<int>;

internal sealed record R09(int Value) : IRequest<int>;

internal sealed record R10(int Value) : IRequest<int>;

internal sealed record R11(int Value) : IRequest<int>;

internal sealed record R12(int Value) : IRequest<int>;

internal sealed record R13(int Value) : IRequest<int>;

internal sealed record R14(int Value) : IRequest<int>;

internal sealed record R15(int Value) : IRequest<int>;

internal sealed record R16(int Value) : IRequest<int>;

internal sealed record R17(int Value) : IRequest<int>;

internal sealed record R18(int Value) : IRequest<int>;

internal sealed record R19(int Value) : IRequest<int>;

internal sealed record R20(int Value) : IRequest<int>;

internal sealed record R21(int Value) : IRequest<int>;

internal sealed record R22(int Value) : IRequest<int>;

internal sealed record R23(int Value) : IRequest<int>;

internal sealed record R24(int Value) : IRequest<int>;

internal sealed record R25(int Value) : IRequest<int>;

internal sealed record R26(int Value) : IRequest<int>;

internal sealed record R27(int Value) : IRequest<int>;

internal sealed record R28(int Value) : IRequest<int>;

internal sealed record R29(int Value) : IRequest<int>;

internal sealed record R30(int Value) : IRequest<int>;

internal sealed record R31(int Value) : IRequest<int>;

internal sealed record R32(int Value) : IRequest<int>;

internal sealed record R33(int Value) : IRequest<int>;

internal sealed record R34(int Value) : IRequest<int>;

internal sealed record R35(int Value) : IRequest<int>;

internal sealed record R36(int Value) : IRequest<int>;

internal sealed record R37(int Value) : IRequest<int>;

internal sealed record R38(int Value) : IRequest<int>;

internal sealed record R39(int Value) : IRequest<int>;

internal sealed record R40(int Value) : IRequest<int>;

internal sealed record R41(int Value) : IRequest<int>;

internal sealed record R42(int Value) : IRequest<int>;

internal sealed record R43(int Value) : IRequest<int>;

internal sealed record R44(int Value) : IRequest<int>;

internal sealed record R45(int Value) : IRequest<int>;

internal sealed record R46(int Value) : IRequest<int>;

internal sealed record R47(int Value) : IRequest<int>;

internal sealed record R48(int Value) : IRequest<int>;

internal sealed record R49(int Value) : IRequest<int>;

internal sealed record R50(int Value) : IRequest<int>;

internal sealed record R51(int Value) : IRequest<int>;

internal sealed record R52(int Value) : IRequest<int>;

internal sealed record R53(int Value) : IRequest<int>;

internal sealed record R54(int Value) : IRequest<int>;

internal sealed record R55(int Value) : IRequest<int>;

internal sealed record R56(int Value) : IRequest<int>;

internal sealed record R57(int Value) : IRequest<int>;

internal sealed record R58(int Value) : IRequest<int>;

internal sealed record R59(int Value) : IRequest<int>;

internal sealed record R60(int Value) : IRequest<int>;

internal sealed record R61(int Value) : IRequest<int>;

internal sealed record R62(int Value) : IRequest<int>;

internal sealed record R63(int Value) : IRequest<int>;
