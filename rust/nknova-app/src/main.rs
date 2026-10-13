use eframe::egui::{self, Align, Color32, RichText};
use nknova_core::probe::{self, HostOs};
use nknova_core::profiles::Profile;
use nknova_core::{Enb, GraphicsOnlyPlan, Settings};
use std::process::Command;

const BACKGROUND: Color32 = Color32::from_rgb(14, 18, 26);
const PANEL: Color32 = Color32::from_rgb(28, 34, 47);
const NAV: Color32 = Color32::from_rgb(20, 25, 35);
const GOLD: Color32 = Color32::from_rgb(248, 183, 75);
const CYAN: Color32 = Color32::from_rgb(88, 200, 211);
const GRAY: Color32 = Color32::from_rgb(156, 170, 189);
const GREEN: Color32 = Color32::from_rgb(107, 226, 166);
const RED: Color32 = Color32::from_rgb(235, 108, 115);

#[derive(Clone, Copy, PartialEq, Eq)]
enum Page { Installer, Profiles, Downloads, Settings }

struct App {
    page: Page,
    settings: Settings,
    plan: Option<GraphicsOnlyPlan>,
    status: String,
    error: bool,
    history: Vec<String>,
}

impl App {
    fn new(cc: &eframe::CreationContext<'_>) -> Self {
        cc.egui_ctx.set_visuals(egui::Visuals::dark());
        let mut style = (*cc.egui_ctx.style_of(egui::Theme::Dark)).clone();
        style.spacing.item_spacing = egui::vec2(10.0, 10.0);
        style.visuals.panel_fill = BACKGROUND;
        style.visuals.widgets.inactive.bg_fill = PANEL;
        style.visuals.widgets.hovered.bg_fill = Color32::from_rgb(53, 65, 84);
        cc.egui_ctx.set_style_of(egui::Theme::Dark, style);
        let (settings, status, error) = match Settings::load() {
            Ok(s) => (s, "Ready — no game files changed".to_owned(), false),
            Err(e) => (Settings::default(), format!("Settings error: {e}"), true),
        };
        Self { page: Page::Installer, settings, plan: None, status,
               error, history: vec!["NKNOVA Rust desktop initialized".to_owned()] }
    }

    fn report(&mut self, message: impl Into<String>, error: bool) {
        let message = message.into();
        self.status = message.clone();
        self.error = error;
        self.history.push(message);
        if self.history.len() > 80 { self.history.remove(0); }
    }

    fn changed(&mut self) {
        self.plan = None;
        self.status = "Configuration changed — run preflight again".into();
        self.error = false;
    }

    fn preflight(&mut self) {
        match GraphicsOnlyPlan::prepare(&self.settings) {
            Ok(plan) => {
                self.report(format!("PASS: Nolvus v6 {} + {}, destination: {}",
                    plan.variant, plan.enb.label(), plan.target.display()), false);
                self.plan = Some(plan);
            }
            Err(message) => { self.plan = None; self.report(format!("BLOCKED: {message}"), true); }
        }
    }

    fn open_existing_installer(&mut self) {
        if let Err(e) = GraphicsOnlyPlan::prepare(&self.settings) {
            self.plan = None;
            self.report(format!("BLOCKED: {e}"), true);
            return;
        }
        match nknova_core::check_legacy_dashboard(&self.settings.legacy_dashboard) {
            Ok(binary) => {
                let mut command = Command::new(&binary);
                if let Some(dir) = binary.parent() { command.current_dir(dir); }
                match command.spawn() {
                    Ok(_) => self.report("Existing Nolvus Dashboard opened. Choose Graphics Only + ENB there and finish the official wizard. Native installation engine is not yet ported.", false),
                    Err(e) => self.report(format!("Launch failed: {e}"), true),
                }
            }
            Err(e) => self.report(format!("BLOCKED: {e}"), true),
        }
    }

    fn panel(ui: &mut egui::Ui, f: impl FnOnce(&mut egui::Ui)) {
        egui::Frame::new()
            .fill(PANEL)
            .corner_radius(egui::CornerRadius::same(14))
            .inner_margin(egui::Margin::same(19))
            .show(ui, f);
    }

    fn path(ui: &mut egui::Ui, label: &str, caption: &str, value: &mut String) -> bool {
        ui.label(RichText::new(label).strong().color(Color32::WHITE));
        ui.label(RichText::new(caption).size(11.0).color(GRAY));
        ui.add(egui::TextEdit::singleline(value).hint_text("Absolute path")
            .desired_width(f32::INFINITY)).changed()
    }

    fn nav(&mut self, ui: &mut egui::Ui) {
        egui::Frame::new().fill(NAV).inner_margin(egui::Margin::same(18))
            .show(ui, |ui| {
                ui.set_min_width(196.0);
                ui.set_min_height(ui.available_height().max(630.0));
                ui.label(RichText::new("◆ NKNOVA").size(27.0).strong().color(GOLD));
                ui.label(RichText::new("RUST EDITION   /   ALPHA").size(10.0).color(GRAY));
                ui.add_space(39.0);
                for (tab, label) in [
                    (Page::Installer, "◈   Install"),
                    (Page::Profiles, "◫   Profiles"),
                    (Page::Downloads, "↓   Downloads"),
                    (Page::Settings, "⚙   Settings"),
                ] {
                    let chosen = self.page == tab;
                    if ui.add(egui::Button::new(
                        RichText::new(label).size(15.0)
                            .color(if chosen { BACKGROUND } else { GRAY }))
                        .fill(if chosen { GOLD } else { NAV })
                        .corner_radius(8.0)
                        .min_size(egui::vec2(190.0, 42.0))
                    ).clicked() { self.page = tab; }
                    ui.add_space(5.0);
                }
                ui.add_space(40.0);
                ui.separator();
                ui.label(RichText::new("HOST SYSTEM").size(11.0).color(GRAY));
                ui.label(RichText::new(HostOs::current().label()).strong().color(CYAN));
                ui.label(RichText::new("Native Rust UI").size(12.0).color(GRAY));
                ui.label(RichText::new("Nolvus API port pending").size(11.0).color(GOLD));
            });
    }

    fn heading(&self, ui: &mut egui::Ui, title: &str, subtitle: &str) {
        ui.horizontal(|ui| {
            ui.vertical(|ui| {
                ui.label(RichText::new("NKNOVA  /  NATIVE").size(11.0).strong().color(CYAN));
                ui.label(RichText::new(title).size(29.0).strong());
                ui.label(RichText::new(subtitle).color(GRAY));
            });
            ui.with_layout(egui::Layout::right_to_left(Align::Center), |ui| {
                ui.label(RichText::new("v0.1  •  RUST").size(12.0).color(GOLD));
            });
        });
        ui.add_space(20.0);
    }

    fn installer(&mut self, ui: &mut egui::Ui) {
        self.heading(ui, "Build your Skyrim.", "One desktop experience. Windows + Linux.");
        Self::panel(ui, |ui| {
            ui.label(RichText::new("01  /  GRAPHICS PROFILE").color(CYAN).strong());
            ui.add_space(6.0);
            ui.label(RichText::new("Nolvus Awakening — Graphics Only").size(24.0).strong());
            ui.label(RichText::new("Official graphics-focused v6 variant, with required mods and ENB. No custom gameplay or AI additions.").color(GRAY));
            ui.add_space(13.0);
            ui.label(RichText::new("SELECT ENB").size(11.0).color(CYAN).strong());
            let previous = self.settings.enb;
            egui::ComboBox::from_id_salt("enb-picker").width(260.0)
                .selected_text(self.settings.enb.label())
                .show_ui(ui, |ui| {
                    for enb in Enb::ALL {
                        ui.selectable_value(&mut self.settings.enb, enb, enb.label());
                    }
                });
            if self.settings.enb != previous { self.changed(); }
        });
        ui.add_space(16.0);
        Self::panel(ui, |ui| {
            ui.label(RichText::new("02  /  INSTALL PATHS").color(CYAN).strong());
            ui.label(RichText::new("No existing instances will be removed, moved or overwritten.").color(GRAY));
            ui.add_space(14.0);
            if Self::path(ui, "STEAM SKYRIM DIRECTORY", "Must contain SkyrimSE.exe", &mut self.settings.game_directory) {
                self.changed();
            }
            if ui.small_button("Detect Steam Skyrim").clicked() {
                match probe::steam_skyrim_candidates().into_iter().next() {
                    Some(game) => {
                        self.settings.game_directory = game.to_string_lossy().into_owned();
                        self.changed();
                        self.report("Found Skyrim in Steam manifest 489830", false);
                    }
                    None => self.report("Skyrim not found in default Steam libraries; select it manually.", true),
                }
            }
            ui.add_space(6.0);
            if Self::path(ui, "NEW NOLVUS TARGET", "Empty dedicated folder (not your current installation)", &mut self.settings.instance_directory) {
                self.changed();
            }
            ui.add_space(6.0);
            if Self::path(ui, "DOWNLOAD ARCHIVES", "Existing separate folder (safe to reuse existing downloads)", &mut self.settings.archive_directory) {
                self.changed();
            }
        });
        ui.add_space(17.0);
        ui.horizontal(|ui| {
            if ui.add(egui::Button::new(RichText::new("✓  VERIFY INSTALL PLAN").strong().color(BACKGROUND))
                .fill(GOLD).corner_radius(8.0).min_size(egui::vec2(215.0, 42.0))
            ).clicked() { self.preflight(); }
            if ui.button("Save preferences").clicked() {
                match self.settings.save() {
                    Ok(()) => self.report("Settings saved to user configuration.", false),
                    Err(e) => self.report(format!("Cannot save settings: {e}"), true),
                }
            }
        });
        ui.add_space(6.0);
        if self.plan.is_some() {
            ui.label(RichText::new("✓ Safe plan validated").color(GREEN));
        } else {
            ui.label(RichText::new("Preflight not passed").color(GRAY));
        }
        if ui.add_enabled(self.plan.is_some() && !self.settings.legacy_dashboard.is_empty(),
            egui::Button::new("Open existing Nolvus installer →").min_size(egui::vec2(260.0, 40.0)))
            .clicked() { self.open_existing_installer(); }
        ui.label(RichText::new("Rust one-click downloader is not available yet. This preview launches the existing official installer only when you explicitly choose it in Settings.").size(11.0).color(GOLD));
        ui.add_space(14.0);
        self.activity(ui);
    }

    fn profiles(&mut self, ui: &mut egui::Ui) {
        self.heading(ui, "Profiles.", "Separate ENB, Community Shaders, NKNOVA and AI packs.");
        for profile in Profile::ALL {
            Self::panel(ui, |ui| {
                ui.horizontal(|ui| {
                    ui.vertical(|ui| {
                        ui.label(RichText::new(profile.label()).strong().size(21.0));
                        ui.label(RichText::new(match profile {
                            Profile::NolvusGraphicsEnb => "Official Nolvus graphics focused variant",
                            Profile::CommunityShaders => "Future graphics profile without ENB conflicts",
                            Profile::NknovaModsAndAi => "Future custom mods + NPC/Mantella integration",
                        }).color(GRAY));
                    });
                    ui.with_layout(egui::Layout::right_to_left(Align::Center), |ui| {
                        ui.label(RichText::new(if profile.implemented() { "PREFLIGHT READY" } else { "COMING NEXT" })
                            .color(if profile.implemented() { GREEN } else { GOLD }).strong());
                    });
                });
            });
            ui.add_space(14.0);
        }
    }

    fn downloads(&mut self, ui: &mut egui::Ui) {
        self.heading(ui, "Downloads.", "Verified packages and resumable downloads are next.");
        Self::panel(ui, |ui| {
            ui.label(RichText::new("ARCHIVE DIRECTORY").color(CYAN).strong());
            ui.label(if self.settings.archive_directory.is_empty() {
                "Not configured"
            } else { &self.settings.archive_directory });
            ui.add_space(15.0);
            ui.label(RichText::new("Native download engine: not implemented").size(21.0).color(GOLD));
            ui.label(RichText::new("Planned: Nexus authorization, SHA256, persistent journal, cancel/resume and rollback. Third-party Skyrim game data is never bundled.").color(GRAY));
        });
        ui.add_space(14.0);
        self.activity(ui);
    }

    fn settings_page(&mut self, ui: &mut egui::Ui) {
        self.heading(ui, "Settings.", "A temporary, explicit bridge to your existing installer.");
        Self::panel(ui, |ui| {
            ui.label(RichText::new(format!("{}  /  Rust native desktop", HostOs::current().label())).color(CYAN));
            ui.add_space(14.0);
            if Self::path(ui, "EXISTING NOLVUS DASHBOARD",
                "Optional: executable NolvusDashboard(.exe), only used for user-requested launch",
                &mut self.settings.legacy_dashboard) { self.changed(); }
            ui.label(RichText::new("No commands are executed through a shell; API keys are not saved or passed to the bridge.").size(11.0).color(GRAY));
            ui.add_space(12.0);
            if ui.button("Save settings").clicked() {
                match self.settings.save() {
                    Ok(()) => self.report("Settings saved.", false),
                    Err(e) => self.report(format!("Cannot save: {e}"), true),
                }
            }
            ui.add_space(12.0);
            ui.label(RichText::new("C# legacy code remains intact until the native Nolvus installer passes real Windows and Linux installation tests.").color(GOLD));
        });
    }

    fn activity(&self, ui: &mut egui::Ui) {
        Self::panel(ui, |ui| {
            ui.label(RichText::new("ACTIVITY LOG").color(CYAN).strong());
            egui::ScrollArea::vertical().max_height(122.0).show(ui, |ui| {
                for message in self.history.iter().rev().take(8) {
                    ui.label(RichText::new(format!("› {message}")).color(GRAY).size(12.0));
                }
            });
        });
    }
}

impl eframe::App for App {
    fn ui(&mut self, ui: &mut egui::Ui, _frame: &mut eframe::Frame) {
        let height = ui.available_height();
        ui.horizontal(|ui| {
            ui.set_min_height(height);
            self.nav(ui);
            ui.add_space(16.0);
            ui.vertical(|ui| {
                ui.set_min_width((ui.available_width() - 22.0).max(550.0));
                egui::ScrollArea::vertical().show(ui, |ui| {
                    ui.add_space(18.0);
                    match self.page {
                        Page::Installer => self.installer(ui),
                        Page::Profiles => self.profiles(ui),
                        Page::Downloads => self.downloads(ui),
                        Page::Settings => self.settings_page(ui),
                    }
                    ui.add_space(14.0);
                    ui.separator();
                    ui.label(RichText::new(&self.status).size(12.0)
                        .color(if self.error { RED } else { GREEN }));
                });
            });
        });
    }
}

fn main() -> eframe::Result {
    eframe::run_native(
        "NKNOVA Rust Dashboard",
        eframe::NativeOptions {
            viewport: egui::ViewportBuilder::default()
                .with_inner_size([1160.0, 890.0])
                .with_min_inner_size([960.0, 660.0]),
            ..Default::default()
        },
        Box::new(|cc| Ok(Box::new(App::new(cc)))),
    )
}
