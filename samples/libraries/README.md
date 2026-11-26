# Sample libraries

Class libraries the sample hosts reference, to exercise cross-assembly discovery (ADR 0001, Cross-assembly discovery). Each references
Keryx itself, so the handler generator runs there and the library carries its manifest (`[assembly: KeryxManifest]` and a
`[KeryxHandler]` on each public handler class).

- `Keryx.Samples.Contracts` (`netstandard2.0`): requests (`PlaceOrder`, `GetOrder`, `CancelOrder`), a stream request (`ListOrders`) and
  notifications (`IOrderEvent`, `OrderPlaced`, `OrderCancelled`). `[assembly: KeryxContracts]` turns off KRX001 there.
- `Keryx.Samples.Orders` (`net10.0`): the handlers of those contracts, two static request behaviors (`Logging`, an open behavior method
  marked `[GlobalBehavior]` and applied in every host that references the library, which injects `IActivityLog`; and `ValidateQuantity`,
  a closed behavior method attached to one handler), and notification handlers, one of them polymorphic (`AuditOrderEvents`, typed with
  `IOrderEvent`).
