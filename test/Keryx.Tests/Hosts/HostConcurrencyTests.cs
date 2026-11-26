using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Tests.Hosts;

/// <summary>
///     Concurrent first use of one scope's mediator (ADR 0001, Mediator): pipeline contexts are created with
///     <c>Volatile.Read(ref field) ?? Interlocked.CompareExchange(ref field, new Context(sp), null) ?? field</c>, so every caller in the
///     scope ends up with the same context and the same handler.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class HostConcurrencyTests : HostTest
{
    private const int Callers = 16;
    private const int Scopes = 50;

    [Test]
    public async Task Concurrent_First_Use_In_One_Scope_Shares_One_Pipeline_Context(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(HostRuntimeTests.Source, cancellationToken);
        var handlerType = host.GetType("Sample.WhoAmIHandler", throwOnError: true)!;

        // A transient handler (AddKeryx keeps an existing registration) makes every context construction observable: each context built
        // gets a new handler with a new Identity, so only a race that publishes a single context gives every caller the same identity.
        await using var provider = BuildProvider(host, services => HostRuntimeTests.RegistrationsWithTransientWhoAmIHandler(host)(services));
        var mediatorType = host.GetType("Sample.KeryxMediator", throwOnError: true)!;
        var whoAmIType = host.GetType("Sample.WhoAmI", throwOnError: true)!;
        var identityType = host.GetType("Sample.Identity", throwOnError: true)!;
        var created = identityType.GetField("Created", BindingFlags.Public | BindingFlags.Static)!;
        var identityId = identityType.GetProperty("Id")!;
        var handlerIdentity = handlerType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Single(f => f.FieldType == identityType);
        var typedSend = mediatorType.GetMethod("Send", [whoAmIType, typeof(CancellationToken)])!;
        var contextField = mediatorType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                                       .Single(f => f.FieldType.GetField("Handler")?.FieldType == handlerType);
        var contextHandler = contextField.FieldType.GetField("Handler")!;

        for (var scopeIndex = 0; scopeIndex < Scopes; scopeIndex++)
        {
            await using var scope = provider.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService(mediatorType);
            var whoAmI = (IRequest<int>)Activator.CreateInstance(whoAmIType)!;
            await Assert.That(contextField.GetValue(mediator)).IsNull();
            var createdBefore = (int)created.GetValue(null)!;

            using var start = new Barrier(Callers);
            var callers = new Task<int>[Callers];
            for (var i = 0; i < Callers; i++)
            {
                // Half the callers use the generic entry point, half the typed overload.
                var generic = i % 2 == 0;
                callers[i] = Task.Factory.StartNew(
                    () =>
                    {
                        start.SignalAndWait(cancellationToken);
                        return generic
                            ? ( (ISender)mediator ).Send(whoAmI, cancellationToken).AsTask().GetAwaiter().GetResult()
                            : ( (ValueTask<int>)typedSend.Invoke(mediator, [whoAmI, cancellationToken])! ).AsTask().GetAwaiter().GetResult();
                    },
                    cancellationToken,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default
                );
            }

            var identities = await Task.WhenAll(callers);
            var context = contextField.GetValue(mediator);

            // Every caller used the context that won the race: one identity, the one held by the stored context's handler. Losers may
            // have built a context each (at most one per caller), but none of them was used.
            await Assert.That(identities.Distinct()).HasSingleItem();
            await Assert.That(context).IsNotNull();
            var storedIdentity = handlerIdentity.GetValue(contextHandler.GetValue(context));
            await Assert.That((int)identityId.GetValue(storedIdentity)!).IsEqualTo(identities[0]);
            await Assert.That((int)created.GetValue(null)! - createdBefore).IsBetween(1, Callers);

            // Later calls reuse that context: no new handler, the same identity.
            await Assert.That(await ( (ISender)mediator ).Send(whoAmI, cancellationToken)).IsEqualTo(identities[0]);
            await Assert.That(contextField.GetValue(mediator)).IsSameReferenceAs(context);
        }
    }
}
