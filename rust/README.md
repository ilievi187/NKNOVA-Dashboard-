# NKNOVA Rust Edition — native desktop rewrite (alpha)

This is a new Rust-native frontend and installer core in the existing
repository. The original Avalonia/.NET Nolvus Dashboard remains preserved
until the very large installation backend can be migrated and proven.

## Working implementation

- Brand-new native egui/eframe interface for Windows/Linux.
- Installer, Profiles, Downloads and Settings pages.
- Cabbage, Kauz, PI-CHO and Amon ENB selection.
- Best-effort Steam Skyrim detection, or manual game path.
- Non-destructive Graphics Only preflight, with refusal of occupied folders,
  path traversal, directory overlap and symlink ancestors.
- Local preferences without stored Nexus tokens or credentials.
- Optional, explicitly selected legacy Dashboard executable handoff.
- Rust unit tests and Windows/Linux executable build workflow.
- Real content-addressed SHA-256 **local archive staging**, background progress and receipt journal.
- Opt-in Nexus legacy v1 account validation and authorized, SHA-pinned HTTPS downloads.
- See [Install Engine phase 1](INSTALL_ENGINE.md) for setup, security restrictions and limitations.

## Critical limitation

The full authenticated Nolvus package downloader, stock-game patcher and
automatic ENB installation have NOT been ported to Rust. The preflight
button checks paths only. However, the Archives page can now audit and stage
SHA-256-pinned authorized files and can opt-in to Nexus v1 authenticated
metadata and approved file downloads. The optional handoff launches the existing
Dashboard; the user must select Graphics Only/ENB and complete the original
installer. No claim of unattended installation is made for the Rust preview.

## Developer build

From the new rust directory, with stable Rust:

    cargo test --workspace
    cargo run --release -p nknova

Linux eframe development packages include libxcb-render0-dev,
libxcb-shape0-dev, libxcb-xfixes0-dev, libxkbcommon-dev and libssl-dev.
On Windows use the MSVC Rust toolchain.

GitHub Actions workflow: NKNOVA Rust Native.
Artifacts: NKNOVA-Rust-windows and NKNOVA-Rust-linux.

## Next migrations

1. Nolvus and Nexus authenticated API adapter plus explicit authorized downloads.
2. Content-addressed archive cache, checksum verification and resume journal.
3. Native game copy/patch, ENB composer, load order and recovery/rollback.
4. Real Windows/Linux clean-install tests before enabling fully one-click.
5. Separate exclusive Community Shaders and NKNOVA custom mods + AI/Mantella.

GPL-3.0-or-later and upstream credits retained. Third-party protected
Skyrim/mod archives are never committed.
