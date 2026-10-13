//! Screenshot gallery with provenance, not fabricated preset comparisons.
//! Author galleries open at the original source. Optional user-authored
//! captures are loaded only from a local per-user folder; no third-party
//! Nexus screenshots are copied or repackaged without permission.

use eframe::egui::{self, Color32, RichText, TextureHandle, TextureOptions, Vec2};
use nknova_core::{settings_file, Enb};
use std::collections::HashMap;
use std::path::{Path, PathBuf};

/// Source mapping is explicitly confirmed by the existing
/// Nolvus.Dashboard/Frames/Installer/v6/ENBFrame.axaml.cs.
fn bundled_asset(enb: Enb) -> &'static [u8] {
    match enb {
        Enb::Cabbage => include_bytes!("../../../Nolvus.Dashboard/Assets/Cabbage_ENB_01.jpg"),
        Enb::Kauz => include_bytes!("../../../Nolvus.Dashboard/Assets/Kauz-ENB.jpg"),
        Enb::Picho => include_bytes!("../../../Nolvus.Dashboard/Assets/PiCho-ENB.jpg"),
        Enb::Amon => include_bytes!("../../../Nolvus.Dashboard/Assets/Amon-ENB.jpg"),
    }
}

pub const SCENES: [(&str, &str); 4] = [
    ("day", "Daylight / exterior"),
    ("forest", "Forest / vegetation"),
    ("night", "Night / lighting"),
    ("interior", "Interior / candles"),
];

pub fn official_gallery(enb: Enb) -> &'static str {
    match enb {
        Enb::Cabbage => "https://www.nexusmods.com/skyrimspecialedition/mods/103042?tab=images",
        Enb::Kauz => "https://www.nexusmods.com/skyrimspecialedition/mods/111431?tab=images",
        Enb::Picho => "https://www.nexusmods.com/skyrimspecialedition/mods/35082?tab=images",
        Enb::Amon => "https://www.nexusmods.com/skyrimspecialedition/mods/99786?tab=images",
    }
}

pub fn author(enb: Enb) -> &'static str {
    match enb {
        Enb::Cabbage => "Cabbage / aaa3561080",
        Enb::Kauz => "Kynias-Kauz",
        Enb::Picho => "HaeVakSa",
        Enb::Amon => "zxamonxz",
    }
}

pub fn description(enb: Enb) -> &'static str {
    match enb {
        Enb::Cabbage => "NAT III-focused atmosphere. Author gallery shows real captured results.",
        Enb::Kauz => "Designed around Nolvus. Author highlights dramatic weather and lighting.",
        Enb::Picho => "Silent Horizons-based, cinematic character and environment lighting.",
        Enb::Amon => "NAT III + Lux preset. Review the creator's original night and daylight scenes.",
    }
}

pub fn preview_root() -> Result<PathBuf, String> {
    Ok(settings_file()?.parent().ok_or("Missing configuration directory")?
        .join("enb-previews"))
}

pub struct Gallery {
    pub scene: usize,
    textures: HashMap<String, TextureHandle>,
    bundled: HashMap<String, TextureHandle>,
    /// Avoid repeated decoding of absent or malformed paths.
    errors: HashMap<String, String>,
}

impl Default for Gallery {
    fn default() -> Self {
        Self { scene: 0, textures: HashMap::new(), bundled: HashMap::new(), errors: HashMap::new() }
    }
}

impl Gallery {
    fn load(&mut self, ctx: &egui::Context, enb: Enb, scene: &str) -> Option<&TextureHandle> {
        let key = format!("{}:{scene}", enb.code());
        if !self.textures.contains_key(&key) && !self.errors.contains_key(&key) {
            match read_preview(ctx, enb, scene) {
                Ok(texture) => { self.textures.insert(key.clone(), texture); }
                Err(message) => { self.errors.insert(key.clone(), message); }
            }
        }
        self.textures.get(&key)
    }

    /// The four original, accurately assigned v6 ENB sample screenshots
    /// already shipped within this NolvusDashboard GPL fork. We reuse those
    /// existing assets as compile-time resources rather than scraping Nexus.
    fn bundled(&mut self, ctx: &egui::Context, enb: Enb) -> Option<&TextureHandle> {
        let key = enb.code().to_owned();
        if !self.bundled.contains_key(&key) {
            let bytes = bundled_asset(enb);
            let decoded = image::load_from_memory(bytes).ok()?.to_rgba8();
            let dims = decoded.dimensions();
            let image = egui::ColorImage::from_rgba_unmultiplied(
                [dims.0 as usize, dims.1 as usize], decoded.as_raw());
            let texture = ctx.load_texture(
                format!("NKNOVA-UPSTREAM-{}", enb.code()), image, TextureOptions::LINEAR);
            self.bundled.insert(key.clone(), texture);
        }
        self.bundled.get(&key)
    }

    pub fn reload(&mut self) {
        self.textures.clear();
        self.bundled.clear();
        self.errors.clear();
    }

    pub fn render(&mut self, ui: &mut egui::Ui, enb: Enb) {
        ui.label(RichText::new(format!("{}  /  ORIGINAL PRESET", enb.label()))
            .strong().size(17.0).color(Color32::WHITE));
        ui.label(RichText::new(description(enb)).color(super::brand::MUTED));
        ui.horizontal_wrapped(|ui| {
            for (index, (_, label)) in SCENES.iter().enumerate() {
                ui.selectable_value(&mut self.scene, index, *label);
            }
        });
        let (scene, scene_title) = SCENES[self.scene];
        let has_local = self.load(ui.ctx(), enb, scene).cloned();
        let texture = has_local.clone().or_else(|| self.bundled(ui.ctx(), enb).cloned());
        if let Some(texture) = texture {
            // Scale down while preserving aspect ratio; never upscale a thumbnail.
            let size = texture.size_vec2();
            let width = size.x.min(ui.available_width()).min(665.0);
            let height = width * size.y / size.x;
            ui.image((texture.id(), Vec2::new(width, height)));
            ui.label(RichText::new(if has_local.is_some() {
                format!("{scene_title} / User-supplied authorized capture / {}", enb.label())
            } else {
                format!("Original Nolvus Dashboard built-in ENB preview / {}", enb.label())
            }).size(11.0).color(super::brand::MUTED));
        } else {
            let (rect, _) = ui.allocate_exact_size(
                Vec2::new(ui.available_width().min(665.0).max(250.0), 90.0),
                egui::Sense::hover(),
            );
            let p = ui.painter_at(rect);
            p.rect_filled(rect, 8.0, super::brand::SURFACE);
            p.text(rect.center(), egui::Align2::CENTER_CENTER,
                format!("No local {scene} screenshot for {}", enb.label()),
                egui::FontId::proportional(13.0), super::brand::MUTED);
            ui.label(RichText::new(
                "The decorative ENB cards are illustrations, NOT game screenshots. Use the actual author gallery below.",
            ).size(11.0).color(super::brand::YELLOW));
        }
        ui.horizontal_wrapped(|ui| {
            ui.hyperlink_to(
                RichText::new("VIEW REAL AUTHOR SCREENSHOTS ↗").color(super::brand::BLUE).strong(),
                official_gallery(enb));
            ui.label(RichText::new(format!("Original gallery: {}", author(enb)))
                .size(11.0).color(super::brand::MUTED));
        });
        ui.label(RichText::new(
            "Want matched day/night/forest/interior comparisons? Add your own scene-specific captures below; they override the built-in screenshot."
        ).size(11.0).color(super::brand::MUTED));
        if let Ok(root) = preview_root() {
            ui.label(RichText::new(format!(
                "Local user captures: {}/{}/{}.png or .jpg",
                root.display(), enb.code().to_ascii_lowercase(), scene
            )).size(10.0).color(super::brand::MUTED));
        }
        if ui.small_button("Reload local screenshots").clicked() { self.reload(); }
    }
}

fn read_preview(ctx: &egui::Context, enb: Enb, scene: &str) -> Result<TextureHandle, String> {
    if !SCENES.iter().any(|(n, _)| *n == scene) {
        return Err("Invalid scene identifier".into());
    }
    let dir = preview_root()?.join(enb.code().to_ascii_lowercase());
    // Never fetch or execute an image from the network; only the local user's
    // explicitly placed PNG/JPEG preview in a dedicated app data folder.
    for ext in ["png", "jpg", "jpeg"] {
        let path = dir.join(format!("{scene}.{ext}"));
        if !path.exists() { continue; }
        check_regular_file(&path)?;
        let meta = std::fs::metadata(&path).map_err(|e| e.to_string())?;
        if meta.len() == 0 || meta.len() > 8 * 1024 * 1024 {
            return Err("Preview image must be 1 byte to 8 MiB".into());
        }
        let (width, height) = image::image_dimensions(&path).map_err(|e| e.to_string())?;
        if width == 0 || height == 0 || width > 4096 || height > 2160 {
            return Err("Preview image exceeds 4096x2160".into());
        }
        let rgba = image::open(&path).map_err(|e| e.to_string())?.to_rgba8();
        let color = egui::ColorImage::from_rgba_unmultiplied(
            [width as usize, height as usize], rgba.as_raw());
        return Ok(ctx.load_texture(
            format!("NKNOVA-{}-{scene}", enb.code()), color, TextureOptions::LINEAR));
    }
    Err("Local screenshot is not present".into())
}

fn check_regular_file(path: &Path) -> Result<(), String> {
    for part in path.ancestors() {
        match std::fs::symlink_metadata(part) {
            Ok(m) if m.file_type().is_symlink() => return Err("Symlink preview path blocked".into()),
            Ok(_) => {}
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => {}
            Err(e) => return Err(format!("Cannot inspect image path: {e}")),
        }
    }
    if !std::fs::symlink_metadata(path).map_err(|e| e.to_string())?
        .file_type().is_file() {
        return Err("Preview is not a regular file".into());
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn original_dashboard_has_a_distinct_jpeg_per_enb() {
        for preset in Enb::ALL {
            let bytes = bundled_asset(preset);
            assert!(bytes.len() > 16_000);
            assert!(bytes.starts_with(&[0xff, 0xd8]));
            assert!(image::load_from_memory(bytes).is_ok());
        }
    }

    #[test]
    fn author_links_are_distinct_and_are_actual_preset_galleries() {
        let links: std::collections::HashSet<_> = Enb::ALL.into_iter()
            .map(official_gallery).collect();
        assert_eq!(links.len(), 4);
        assert!(links.iter().all(|s| s.starts_with("https://www.nexusmods.com/skyrimspecialedition/mods/")));
    }
}
