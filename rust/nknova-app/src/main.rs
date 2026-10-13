mod brand;

use eframe::egui::{self, Align, Color32, RichText};
use nknova_core::probe::{self, HostOs};
use nknova_core::profiles::Profile;
use nknova_core::{Enb, GraphicsOnlyPlan, Settings};
use std::process::Command;

const BACKGROUND: Color32 = brand::NIGHT;
const PANEL: Color32 = brand::SURFACE;
const NAV: Color32 = brand::RAIL;
const GOLD: Color32 = brand::YELLOW;
const CYAN: Color32 = brand::BLUE;
const GRAY: Color32 = brand::MUTED;
const GREEN: Color32 = brand::GREEN;
const RED: Color32 = brand::RED;

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
        brand::apply(&cc.egui_ctx);
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
            .stroke(egui::Stroke::new(1.0, brand::OUTLINE))
            .corner_radius(egui::CornerRadius::same(12))
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
                // A fixed rail width prevents the banner underline from consuming
                // the entire eframe horizontal layout and clipping the main page.
                ui.set_width(224.0);
                ui.set_min_height(ui.available_height().max(630.0));
                brand::sidebar_brand(ui);
                ui.add_space(31.0);
                ui.label(RichText::new("MISSION CONTROL").monospace().size(10.0).color(CYAN));
                ui.add_space(8.0);
                for (tab, label) in [
                    (Page::Installer, "01  /  DEPLOY"),
                    (Page::Profiles, "02  /  MOD PROFILES"),
                    (Page::Downloads, "03  /  ARCHIVES"),
                    (Page::Settings, "04  /  SYSTEM"),
                ] {
                    let chosen = self.page == tab;
                    if ui.add(egui::Button::new(
                        RichText::new(label).size(15.0)
                            .color(if chosen { BACKGROUND } else { GRAY }))
                        .fill(if chosen { GOLD } else { NAV })
                        .corner_radius(6.0)
                        .min_size(egui::vec2(218.0, 42.0))
                    ).clicked() { self.page = tab; }
                    ui.add_space(5.0);
                }
                ui.add_space(40.0);
                ui.separator();
                ui.label(RichText::new("HOST SYSTEM").size(11.0).color(GRAY));
                ui.label(RichText::new(HostOs::current().label()).strong().color(CYAN));
                ui.label(RichText::new("RUST NATIVE  //  ALPHA").monospace().size(11.0).color(GRAY));
                ui.label(RichText::new("INSTALL ENGINE  /  IN PROGRESS").monospace().size(10.0).color(GOLD));
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
        brand::hero(ui);
        ui.add_space(10.0);
        brand::channel_link(ui);
        ui.add_space(16.0);
        Self::panel(ui, |ui| {
            brand::section_label(ui, "01", "GRAPHICS PROFILE", "NOLVUS AWAKENING  /  V6");
            ui.add_space(6.0);
            ui.label(RichText::new("Choose your ENB atmosphere").size(23.0).strong().color(brand::TEXT));
            ui.label(RichText::new("Official Graphics Only variant. Separate installation, no extra gameplay or AI mods.").color(GRAY));
            ui.add_space(13.0);
            if brand::enb_selector(ui, &mut self.settings.enb) { self.changed(); }
            ui.add_space(5.0);
            ui.label(RichText::new(format!("PRESET LOCKED TO:  {}", self.settings.enb.code()))
                .monospace().size(11.0).color(GOLD));
        });
        ui.add_space(16.0);
        Self::panel(ui, |ui| {
            brand::section_label(ui, "02", "INSTALL TARGETS", "SAFE / READ-ONLY PREFLIGHT");
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
                .fill(GOLD).corner_radius(5.0).min_size(egui::vec2(215.0, 44.0))
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
        ui.label(RichText::new("ALPHA NOTICE  /  Native one-click downloading is not implemented. The optional handoff opens the existing installer only when explicitly configured.").monospace().size(10.0).color(GOLD));
        ui.add_space(14.0);
        self.activity(ui);
    }

    fn profiles(&mut self, ui: &mut egui::Ui) {
        self.heading(ui, "MOD PROFILES", "Nolvus, graphics engines and the future NKNOVA expansion.");
        for profile in Profile::ALL {
            Self::panel(ui, |ui| {
                ui.horizontal(|ui| {
                    ui.vertical(|ui| {
                        ui.label(RichText::new(profile.label()).strong().size(21.0).color(brand::TEXT));
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
        self.heading(ui, "THE ARCHIVE", "Local mod cache, protected downloads and integrity checks.");
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
        self.heading(ui, "SYSTEM SETTINGS", "Platform, directories and temporary migration bridge.");
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
            brand::section_label(ui, "SYS", "OPERATION LOG", "NKNOVA / RUST");
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
        ui.with_layout(egui::Layout::left_to_right(Align::Min), |ui| {
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
        "NKNOVA // NIGHTKNIGHT MOD LAB",
        eframe::NativeOptions {
            viewport: egui::ViewportBuilder::default()
                .with_inner_size([1260.0, 920.0])
                .with_min_inner_size([1060.0, 680.0]),
            ..Default::default()
        },
        Box::new(|cc| Ok(Box::new(App::new(cc)))),
    )
}
