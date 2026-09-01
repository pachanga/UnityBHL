# BHL for Unity

Unity integration for [BHL](https://github.com/bitdotgames/BHL): attach BHL script classes to
GameObjects via `ScriptBHL`, with hot reload of edited `.bhl` files while in Play Mode.

## Requirements

- Unity 2021.3+
- The `com.bitgames.bhl` package (BHL core)

## BHLComponent (optional base class)

`unity.BHLComponent` (part of the `unity` bindings module, no extra import/config needed)
declares an optional base class with `gameObject`/`transform` fields and virtual no-op
`Awake`/`Update`/`OnDestroy` methods. Extending it isn't required - `ScriptBHL` detects these
fields/methods by name either way - but it makes them IDE/LSP-discoverable and gives the
lifecycle methods an explicit, overridable contract:

```bhl
import "unity"

class MyScript : unity.BHLComponent
{
  override func Update() { ... }
}
```

## Editor tools

- **BHL/Control Panel** - compile status, errors, auto-compile toggle, VM pool stats.
- **BHL/Rebuild** - clean compile and baking a bytecode bundle for
  Player builds.

## License

MIT - see [LICENSE](LICENSE).
