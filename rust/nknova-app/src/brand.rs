//! NightKnight / NKNOVA visual identity, rendered as native egui vector art.
//! No third-party imagery or YouTube assets are bundled.
//! All accent art works with the existing core's preflight and does not simulate progress.

use eframe::egui::{self, Align2, Color32, FontId, Pos2, Rect, RichText, Sense, Shape, Stroke, StrokeKind, Vec2};
use nknova_core::Enb;

pub const NIGHT: Color32 = Color32::from_rgb(7, 12, 21);
pub const SURFACE: Color32 = Color32::from_rgb(14, 23, 37);
pub const RAIL: Color32 = Color32::from_rgb(9, 18, 32);
pub const BLUE: Color32 = Color32::from_rgb(44, 151, 248);
pub const YELLOW: Color32 = Color32::from_rgb(255, 205, 61);
pub const TEXT: Color32 = Color32::from_rgb(241, 248, 255);
pub const MUTED: Color32 = Color32::from_rgb(155, 177, 199);
pub const GREEN: Color32 = Color32::from_rgb(110, 225, 164);
pub const RED: Color32 = Color32::from_rgb(245, 106, 115);
pub const OUTLINE: Color32 = Color32::from_rgb(41, 62, 82);
pub const YOUTUBE: &str = "https://www.youtube.com/@N1GH7KN1GH7";

pub fn apply(ctx: &egui::Context) {
    let mut visuals = egui::Visuals::dark();
    visuals.panel_fill = NIGHT;
    visuals.window_fill = SURFACE;
    visuals.extreme_bg_color = NIGHT;
    visuals.faint_bg_color = SURFACE;
    visuals.widgets.noninteractive.bg_fill = SURFACE;
    visuals.widgets.noninteractive.fg_stroke.color = MUTED;
    visuals.widgets.inactive.bg_fill = Color32::from_rgb(23, 38, 57);
    visuals.widgets.inactive.fg_stroke.color = TEXT;
    visuals.widgets.hovered.bg_fill = Color32::from_rgb(30, 65, 101);
    visuals.widgets.hovered.fg_stroke.color = TEXT;
    visuals.widgets.active.bg_fill = BLUE;
    visuals.widgets.active.fg_stroke.color = NIGHT;
    visuals.selection.bg_fill = Color32::from_rgb(34, 95, 156);
    visuals.selection.stroke.color = YELLOW;
    ctx.set_visuals(visuals);
    let mut style = (*ctx.style_of(egui::Theme::Dark)).clone();
    style.spacing.item_spacing = egui::vec2(10.0, 11.0);
    style.spacing.button_padding = egui::vec2(14.0, 9.0);
    style.visuals = ctx.style_of(egui::Theme::Dark).visuals.clone();
    ctx.set_style_of(egui::Theme::Dark, style);
}

fn polygon(p: &egui::Painter, points: Vec<Pos2>, color: Color32) {
    p.add(Shape::convex_polygon(points, color, Stroke::NONE));
}

fn line(p: &egui::Painter, start: Pos2, end: Pos2, color: Color32, width: f32) {
    p.line_segment([start, end], Stroke::new(width, color));
}

/// Custom angular helmet/crest derived from N and K, not an imported logo.
fn knight_glyph(p: &egui::Painter, rect: Rect, color: Color32) {
    let x = rect.center().x;
    let y = rect.center().y;
    let s = (rect.width().min(rect.height()) / 100.0).max(0.2);
    let point = |px: f32, py: f32| Pos2::new(x + px * s, y + py * s);
    let stroke = Stroke::new(2.7 * s, color);
    p.add(Shape::line(vec![
        point(-37.0, -40.0), point(-18.0, -33.0), point(0.0, -43.0),
        point(18.0, -33.0), point(37.0, -40.0), point(28.0, 12.0),
        point(0.0, 44.0), point(-28.0, 12.0), point(-37.0, -40.0),
    ], stroke));
    // N / K integrated in an angular visor.
    p.add(Shape::line(vec![
        point(-22.0, 18.0), point(-22.0, -21.0), point(-4.0, 11.0),
        point(-4.0, -21.0),
    ], stroke));
    p.add(Shape::line(vec![
        point(7.0, -21.0), point(7.0, 18.0),
    ], stroke));
    p.add(Shape::line(vec![
        point(26.0, -21.0), point(8.0, -1.0), point(26.0, 18.0),
    ], stroke));
    line(p, point(-22.0, 27.0), point(22.0, 27.0), color, 1.0 * s);
}

pub fn sidebar_brand(ui: &mut egui::Ui) {
    ui.horizontal(|ui| {
        let (rect, _) = ui.allocate_exact_size(Vec2::new(68.0, 70.0), Sense::hover());
        let p = ui.painter_at(rect);
        knight_glyph(&p, rect.shrink(5.0), YELLOW);
        ui.vertical(|ui| {
            ui.add_space(11.0);
            ui.label(RichText::new("NKNOVA").size(25.0).strong().color(TEXT));
            ui.label(RichText::new("BY NIGHTKNIGHT").monospace().size(10.0).color(BLUE));
        });
    });
    ui.add_space(3.0);
    ui.label(RichText::new("N1GH7KN1GH7  //  MOD LAB")
        .monospace().size(10.0).color(MUTED));
    ui.add_space(6.0);
    let (stripe, _) = ui.allocate_exact_size(Vec2::new(ui.available_width(), 3.0), Sense::hover());
    let p = ui.painter_at(stripe);
    let a = Rect::from_min_size(stripe.min, Vec2::new(stripe.width() * 0.62, 3.0));
    let b = Rect::from_min_max(Pos2::new(a.max.x, stripe.min.y), stripe.max);
    p.rect_filled(a, 0.0, BLUE);
    p.rect_filled(b, 0.0, YELLOW);
}

/// Hero rendered with vectors: esports broadcast bars, shield, halo, night sky.
pub fn hero(ui: &mut egui::Ui) {
    let width = ui.available_width().max(500.0);
    let (rect, _) = ui.allocate_exact_size(Vec2::new(width, 223.0), Sense::hover());
    let painter = ui.painter_at(rect);
    painter.rect_filled(rect, 14.0, SURFACE);
    painter.rect_stroke(rect.shrink(0.5), 14.0, Stroke::new(1.0, OUTLINE), StrokeKind::Inside);

    // Stacked blue-tinted cinematic plates.
    let w = rect.width();
    let x = rect.min.x;
    let y = rect.min.y;
    polygon(&painter, vec![
        Pos2::new(x + w * 0.52, y),
        Pos2::new(x + w * 0.77, y),
        Pos2::new(x + w * 0.57, y + rect.height()),
        Pos2::new(x + w * 0.31, y + rect.height()),
    ], Color32::from_rgb(16, 42, 69));
    polygon(&painter, vec![
        Pos2::new(x + w * 0.78, y),
        Pos2::new(rect.max.x, y),
        Pos2::new(rect.max.x, rect.max.y),
        Pos2::new(x + w * 0.61, rect.max.y),
    ], Color32::from_rgb(14, 34, 60));
    for i in 0..10 {
        let px = x + w * 0.57 + i as f32 * 28.0;
        line(&painter,
            Pos2::new(px, y + 12.0),
            Pos2::new(px - 72.0, rect.max.y - 12.0),
            Color32::from_rgba_unmultiplied(44, 151, 248, 35),
            1.0);
    }

    let center = Pos2::new(x + w * 0.79, rect.center().y);
    painter.circle_stroke(center, 87.0, Stroke::new(2.0, Color32::from_rgb(29, 78, 116)));
    painter.circle_stroke(center, 101.0, Stroke::new(1.0, Color32::from_rgb(28, 56, 80)));
    painter.circle_stroke(center, 70.0, Stroke::new(1.0, BLUE));
    knight_glyph(&painter, Rect::from_center_size(center, Vec2::splat(140.0)), YELLOW);
    line(&painter, Pos2::new(center.x - 100.0, center.y),
        Pos2::new(center.x + 100.0, center.y), Color32::from_rgba_unmultiplied(44, 151, 248, 60), 1.0);
    line(&painter, Pos2::new(center.x, center.y - 104.0),
        Pos2::new(center.x, center.y + 104.0), Color32::from_rgba_unmultiplied(44, 151, 248, 60), 1.0);

    painter.rect_filled(Rect::from_min_size(Pos2::new(x, y), Vec2::new(6.0, 223.0)), 0.0, YELLOW);
    painter.text(Pos2::new(x + 28.0, y + 22.0), Align2::LEFT_TOP,
        "NIGHTKNIGHT  //  PROJECT 001", FontId::monospace(12.0), BLUE);
    painter.text(Pos2::new(x + 26.0, y + 49.0), Align2::LEFT_TOP,
        "NKNOVA", FontId::proportional(43.0), TEXT);
    painter.text(Pos2::new(x + 28.0, y + 103.0), Align2::LEFT_TOP,
        "MOD YOUR WORLD.", FontId::proportional(21.0), YELLOW);
    painter.text(Pos2::new(x + 28.0, y + 141.0), Align2::LEFT_TOP,
        "THE SKYRIM MODDING PLATFORM", FontId::monospace(12.0), MUTED);
    painter.text(Pos2::new(x + 28.0, y + 176.0), Align2::LEFT_TOP,
        "RUST NATIVE    /    WINDOWS + LINUX", FontId::monospace(11.0), BLUE);

    painter.rect_filled(
        Rect::from_min_size(Pos2::new(x + 24.0, rect.max.y - 7.0),
            Vec2::new((w * 0.37).min(320.0), 3.0)), 0.0, BLUE);
    painter.rect_filled(
        Rect::from_min_size(Pos2::new(x + 24.0 + (w * 0.37).min(320.0), rect.max.y - 7.0),
            Vec2::new(67.0, 3.0)), 0.0, YELLOW);
}

/// Four real UI selections, rendered with distinct procedural scenic thumbnails.
/// We intentionally do NOT import unrelated fantasy game artwork or unverified
/// public YouTube avatar images as channel-owned assets.
pub fn enb_selector(ui: &mut egui::Ui, selection: &mut Enb) -> bool {
    let mut changed = false;
    let available = ui.available_width();
    let card_width = ((available - 33.0) / 4.0).max(126.0);
    ui.horizontal(|ui| {
        for (index, enb) in Enb::ALL.iter().copied().enumerate() {
            let size = Vec2::new(card_width, 147.0);
            let (rect, response) = ui.allocate_exact_size(size, Sense::click());
            let painter = ui.painter_at(rect);
            let selected = *selection == enb;
            let accent = match enb {
                Enb::Cabbage => Color32::from_rgb(63, 157, 246),
                Enb::Kauz => Color32::from_rgb(243, 170, 81),
                Enb::Picho => Color32::from_rgb(167, 135, 236),
                Enb::Amon => Color32::from_rgb(106, 213, 177),
            };
            let base = match enb {
                Enb::Cabbage => Color32::from_rgb(19, 58, 91),
                Enb::Kauz => Color32::from_rgb(74, 45, 44),
                Enb::Picho => Color32::from_rgb(50, 40, 85),
                Enb::Amon => Color32::from_rgb(22, 65, 67),
            };
            let bg = if response.hovered() {
                Color32::from_rgb(33, 52, 75)
            } else {
                SURFACE
            };
            painter.rect_filled(rect, 11.0, bg);
            let image = Rect::from_min_max(
                rect.min + Vec2::new(5.0, 5.0),
                Pos2::new(rect.max.x - 5.0, rect.min.y + 98.0)
            );
            painter.rect_filled(image, 7.0, base);
            let sun = Pos2::new(image.min.x + image.width() * 0.70,
                image.min.y + 33.0);
            painter.circle_filled(sun, 19.0, accent.gamma_multiply(0.46));
            painter.circle_stroke(sun, 26.0, Stroke::new(1.0, accent.gamma_multiply(0.5)));
            let mountain = |a: (f32,f32), b: (f32,f32), c: (f32,f32), tone| {
                polygon(&painter, vec![
                    Pos2::new(image.min.x + image.width() * a.0,
                        image.min.y + image.height() * a.1),
                    Pos2::new(image.min.x + image.width() * b.0,
                        image.min.y + image.height() * b.1),
                    Pos2::new(image.min.x + image.width() * c.0,
                        image.min.y + image.height() * c.1),
                ], tone);
            };
            mountain((0.0, 0.97), (0.36, 0.22), (0.76, 0.97),
                accent.gamma_multiply(0.34));
            mountain((0.30, 0.97), (0.74, 0.31), (1.0, 0.97),
                Color32::from_rgb(12, 27, 46));
            // A stylized watchtower; drawn on the thumbnail, no external images.
            let tx = image.min.x + image.width() * 0.23;
            let ty = image.min.y + 41.0 + (index as f32 * 3.0);
            painter.rect_filled(Rect::from_min_size(Pos2::new(tx, ty),
                Vec2::new(9.0, image.max.y - ty)), 0.0, Color32::from_rgb(8, 15, 29));
            polygon(&painter, vec![
                Pos2::new(tx-4.0, ty), Pos2::new(tx+4.5, ty-13.0),
                Pos2::new(tx+13.0, ty),
            ], Color32::from_rgb(8, 15, 29));
            painter.text(Pos2::new(rect.min.x + 12.0, rect.min.y + 109.0),
                Align2::LEFT_TOP, enb.label(), FontId::proportional(14.0),
                if selected { YELLOW } else { TEXT });
            painter.text(Pos2::new(rect.max.x - 11.0, rect.min.y + 12.0),
                Align2::RIGHT_TOP, if selected { "SELECTED" } else { "" },
                FontId::monospace(9.0), YELLOW);
            painter.rect_stroke(rect.shrink(1.0), 11.0, Stroke::new(
                if selected { 2.0 } else { 1.0 },
                if selected { YELLOW } else { OUTLINE }),
                StrokeKind::Inside);
            if response.clicked() {
                *selection = enb;
                changed = true;
            }
            response.on_hover_text(format!("Use {} — {}", enb.label(), enb.code()));
        }
    });
    changed
}

pub fn section_label(ui: &mut egui::Ui, serial: &str, title: &str, info: &str) {
    ui.horizontal(|ui| {
        ui.label(RichText::new(serial).monospace().size(12.0).strong().color(YELLOW));
        ui.label(RichText::new(title).strong().size(13.0).color(TEXT));
        ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
            ui.label(RichText::new(info).monospace().size(10.0).color(BLUE));
        });
    });
}

pub fn channel_link(ui: &mut egui::Ui) {
    ui.horizontal(|ui| {
        ui.label(RichText::new("INSPIRED BY NIGHTKNIGHT").monospace().size(11.0).color(MUTED));
        ui.hyperlink_to(RichText::new("▶  @N1GH7KN1GH7").size(12.0).color(YELLOW), YOUTUBE);
    });
}
