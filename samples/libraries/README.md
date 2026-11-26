# Sample libraries

Class libraries the sample hosts reference, to exercise cross-assembly discovery (ADR 0001, Cross-assembly discovery). Each references
Keryx itself, so the handler generator runs there and the library carries its manifest (`[assembly: KeryxManifest]` and a
`[KeryxHandler]` on each public handler class).

- `Keryx.Samples.Contracts` (`netstandard2.0`): the order notifications (`IOrderEvent`, `OrderPlaced`, `OrderCancelled`). It declares no
  requests: a request's or stream request's handler lives in the request's own assembly (KRX001 there, KRX023 elsewhere), while a
  notification may be handled in any assembly.
- `Keryx.Samples.Orders` (`net10.0`): the requests (`PlaceOrder`, `GetOrder`, `CancelOrder`), the stream request (`ListOrders`) and their
  `OrderSummary` response, next to their handlers; two static request behaviors (`Logging`, an open behavior method marked
  `[GlobalBehavior]` and applied in every host that references the library, which injects `IActivityLog`; and `ValidateQuantity`, a closed
  behavior method attached to one handler); and handlers of the contracts' notifications, one of them polymorphic (`AuditOrderEvents`,
  typed with `IOrderEvent`).
