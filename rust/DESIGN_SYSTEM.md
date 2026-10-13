# NightKnight design system — NKNOVA Rust UI v2

## Identity

**NKNOVA / NIGHTKNIGHT MOD LAB** — a cinematic PC modding interface,
not an unrelated off-the-shelf dashboard theme.

- Core: near-black navy `#070C15`, surfaces `#0E1725` and rail `#091220`.
- Primary action: warm NightKnight yellow `#FFCD3D`, reserved for key actions,
  selected ENBs and the glyph.
- Secondary signals: electric blue `#2C97F8` for system and workflow labels.
- Text: ice white `#F1F8FF`; muted blue-gray `#9BB1C7`.
- Typography: contrasting large cinematic headlines, short uppercase telemetry
  and monospace details. No third-party font license dependencies.
- Brand element: original vector shield + stylized N/K visor inside the app.
  This is original procedural UI artwork, not a copied logo or a purchased icon.

## YouTube reference

Channel identity: **@N1GH7KN1GH7**
https://www.youtube.com/@N1GH7KN1GH7

The NightKnight handle and its gaming/content-creation direction are referenced
in the dashboard, with a clickable channel link. The exact channel avatar and
banner could not be retrieved/verified automatically. No unrelated 'Night
Knight' game images, channel identities or scraped copyrighted art are bundled.

If the channel's actual avatar/banner assets are supplied by its owner, replace
the procedural crest/banner through a separate verified asset integration.

## Implementation

`rust/nknova-app/src/brand.rs` holds all visual tokens and artwork, with
one original cinematic vector hero, four *interactive* procedural ENB scenes
and a compact custom sidebar crest. Core path safety, Steam discovery,
preflight, settings and optional legacy handoff are unchanged.

The preview does **not** claim Nolvus downloads or installation are completed:
the native installer is still under development. Never display fake progress
or fabricated platform/environment status.

## Visual quality gates

- Text remains legible on all colored surfaces.
- Distinct interactive hover and selected ENB states.
- Windows/Linux compilation and unchanged Rust preflight tests.
- No additional runtime fonts, internet image fetches or JavaScript bridges.
