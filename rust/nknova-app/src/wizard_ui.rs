use super::*;
use nknova_core::{InstallVariant, ResourceLimits};

#[derive(Clone, Copy, PartialEq, Eq)]
pub(super) enum WizardStep {
    Edition, Visuals, Paths, Resources, Review,
}
impl WizardStep {
    const ALL: [Self; 5] = [Self::Edition, Self::Visuals, Self::Paths, Self::Resources, Self::Review];
    fn label(self) -> &'static str {
        match self {
            Self::Edition => "01 / EDITION",
            Self::Visuals => "02 / VISUALS",
            Self::Paths => "03 / PATHS",
            Self::Resources => "04 / LIMITS",
            Self::Review => "05 / REVIEW",
        }
    }
}

impl App {
    pub(super) fn installer(&mut self, ui: &mut egui::Ui) {
        if self.wizard_step == WizardStep::Edition {
            brand::hero(ui);
            ui.add_space(8.0);
            brand::channel_link(ui);
        } else {
            self.heading(ui, "BUILD YOUR SKYRIM", "Choose now; validate the actual game later.");
        }
        ui.add_space(12.0);
        ui.horizontal_wrapped(|ui| {
            for step in WizardStep::ALL {
                let selected = self.wizard_step == step;
                if ui.add(egui::Button::new(RichText::new(step.label())
                    .monospace().size(11.0)
                    .color(if selected { BACKGROUND } else { brand::TEXT }))
                    .fill(if selected { GOLD } else { PANEL }))
                    .clicked() {
                    self.wizard_step = step;
                }
            }
        });
        ui.add_space(14.0);
        match self.wizard_step {
            WizardStep::Edition => self.edition_screen(ui),
            WizardStep::Visuals => self.visuals_screen(ui),
            WizardStep::Paths => self.paths_screen(ui),
            WizardStep::Resources => self.resources_screen(ui),
            WizardStep::Review => self.review_screen(ui),
        }
        ui.add_space(15.0);
        ui.horizontal(|ui| {
            let idx = WizardStep::ALL.iter().position(|s| *s == self.wizard_step).unwrap_or(0);
            if ui.add_enabled(idx > 0, egui::Button::new("← BACK")).clicked() {
                self.wizard_step = WizardStep::ALL[idx - 1];
            }
            if ui.add_enabled(idx < 4, egui::Button::new(
                RichText::new("NEXT STEP →").color(BACKGROUND).strong()).fill(GOLD))
                .clicked() {
                self.wizard_step = WizardStep::ALL[(idx + 1).min(4)];
            }
        });
        ui.add_space(14.0);
        self.activity(ui);
    }

    fn edition_screen(&mut self, ui: &mut egui::Ui) {
        Self::panel(ui, |ui| {
            brand::section_label(ui, "01", "NOLVUS AWAKENING V6", "FOUR DISTINCT EDITIONS");
            ui.label(RichText::new("Choose your edition")
                .size(23.0).strong().color(brand::TEXT));
            ui.label(RichText::new(
                "You can configure every edition without Skyrim installed. Graphics Only, Redux, Ultra and Ultimate are exclusive choices."
            ).color(GRAY));
            ui.add_space(13.0);
            for two in InstallVariant::ALL.chunks(2) {
                ui.columns(2, |cols| {
                    for (i, variant) in two.iter().copied().enumerate() {
                        let selected = self.settings.variant == variant;
                        let (size, download) = variant.official_sizes_gb();
                        egui::Frame::new()
                            .fill(if selected { Color32::from_rgb(29, 50, 69) } else { brand::NIGHT })
                            .stroke(egui::Stroke::new(if selected { 2.0 } else { 1.0 },
                                if selected { GOLD } else { brand::OUTLINE }))
                            .corner_radius(8)
                            .inner_margin(egui::Margin::same(12))
                            .show(&mut cols[i], |ui| {
                                if ui.add(
                                    egui::Button::new(RichText::new(format!(
                                        "{}{}",
                                        if selected { "SELECTED / " } else { "CHOOSE / " },
                                        variant.label()
                                    )).size(18.0).strong().color(
                                        if selected { GOLD } else { brand::TEXT }
                                    ))
                                    .fill(Color32::TRANSPARENT)
                                    .stroke(egui::Stroke::NONE)
                                ).clicked() {
                                    self.settings.variant = variant;
                                    self.changed();
                                }
                                ui.label(RichText::new(variant.description()).size(12.0).color(GRAY));
                                ui.label(RichText::new(format!(
                                    "~{size} GB installed  |  ~{download} GB download"
                                )).monospace().size(11.0).color(CYAN));
                                ui.label(RichText::new(if variant.requires_full_backend() {
                                    "NATIVE INSTALL: NOT YET IMPLEMENTED"
                                } else { "GRAPHICS-ONLY PATH PREFLIGHT: READY" })
                                    .size(10.0).color(if variant.requires_full_backend() {
                                        GOLD
                                    } else { GREEN }));
                            });
                    }
                });
                ui.add_space(8.0);
            }
            if ui.checkbox(&mut self.settings.sr_exterior_cities,
                "Enable SR Exterior Cities (higher VRAM needs)").changed() {
                self.changed();
            }
            ui.hyperlink_to(RichText::new("OFFICIAL NOLVUS REQUIREMENTS ↗").color(CYAN),
                "https://www.nolvus.net/appendix/installer/requirements");
        });
    }

    fn visuals_screen(&mut self, ui: &mut egui::Ui) {
        Self::panel(ui, |ui| {
            brand::section_label(ui, "02", "CHOOSE ENB", "REAL AUTHOR GALLERIES");
            ui.label(RichText::new("Pick the look you like").size(23.0)
                .strong().color(brand::TEXT));
            ui.label(RichText::new(
                "The ENB cards are stylized illustrations; use the linked original galleries to compare actual game captures."
            ).size(12.0).color(GRAY));
            ui.add_space(11.0);
            if brand::enb_selector(ui, &mut self.settings.enb) {
                self.changed();
            }
            ui.add_space(16.0);
            self.gallery.render(ui, self.settings.enb);
        });
    }

    fn paths_screen(&mut self, ui: &mut egui::Ui) {
        Self::panel(ui, |ui| {
            brand::section_label(ui, "03", "INSTALL DIRECTORIES", "EXISTING MODLISTS PROTECTED");
            ui.label(RichText::new("You don't need Skyrim to explore NKNOVA.")
                .color(brand::TEXT).strong());
            ui.label(RichText::new(
                "When installing, select a legitimate Steam Skyrim SE/AE folder and a NEW empty modlist directory."
            ).color(GRAY));
            ui.add_space(13.0);
            if Self::path(ui, "STEAM SKYRIM DIRECTORY",
                "Requires SkyrimSE.exe for installation, optional while previewing",
                &mut self.settings.game_directory) {
                self.changed();
            }
            if ui.small_button("Detect Steam Skyrim").clicked() {
                match probe::steam_skyrim_candidates().into_iter().next() {
                    Some(game) => {
                        self.settings.game_directory = game.to_string_lossy().into_owned();
                        self.changed();
                        self.report("Found Skyrim in Steam manifest 489830", false);
                    }
                    None => self.report("No Skyrim detected. Preview and preference saving still work.", false),
                }
            }
            ui.add_space(7.0);
            if Self::path(ui, "NEW NOLVUS TARGET",
                "Empty separate folder — never overwrite an existing instance",
                &mut self.settings.instance_directory) {
                self.changed();
            }
            ui.add_space(7.0);
            if Self::path(ui, "ARCHIVE CACHE",
                "Existing folder for authorized mod archives",
                &mut self.settings.archive_directory) {
                self.changed();
            }
        });
    }

    fn resources_screen(&mut self, ui: &mut egui::Ui) {
        Self::panel(ui, |ui| {
            brand::section_label(ui, "04", "BACKGROUND WORKLOAD", "NEXUS SPEED LIMIT ENFORCED");
            ui.label(RichText::new("Keep the computer usable").size(23.0)
                .strong().color(brand::TEXT));
            ui.label(RichText::new(
                "Nexus CDN download speed is genuinely limited. RAM, CPU, concurrency, retry and background preferences are saved for the future scheduler — not yet enforced."
            ).size(12.0).color(GRAY));
            ui.add_space(12.0);
            let lim = &mut self.settings.limits;
            let mut changed = false;
            changed |= ui.checkbox(&mut lim.background_mode,
                "Prefer background priority (future scheduler)").changed();
            changed |= ui.checkbox(&mut lim.pause_after_download,
                "Stop before installing after download (future scheduler)").changed();
            ui.label(RichText::new("REAL NEXUS DOWNLOAD CAP  /  0 = UNLIMITED")
                .monospace().size(11.0).color(CYAN));
            changed |= ui.add(egui::Slider::new(&mut lim.max_download_mib_s, 0..=500)
                .text("MiB/s")).changed();
            ui.label(RichText::new(format!("Current bandwidth setting: {}",
                if lim.max_download_mib_s == 0 { "Unlimited".to_owned() }
                else { format!("{} MiB/s", lim.max_download_mib_s) }))
                .size(12.0).color(GREEN));
            ui.add_space(9.0);
            ui.separator();
            ui.label(RichText::new("FUTURE INSTALL SCHEDULER / SAVED ONLY")
                .monospace().size(11.0).color(GOLD));
            changed |= ui.add(egui::Slider::new(&mut lim.concurrent_downloads, 1..=16)
                .text("Concurrent downloads")).changed();
            changed |= ui.add(egui::Slider::new(&mut lim.max_ram_gib, 1..=256)
                .text("RAM budget (GiB)")).changed();
            changed |= ui.add(egui::Slider::new(&mut lim.cpu_threads, 0..=64)
                .text("CPU worker threads (0 = auto)")).changed();
            changed |= ui.add(egui::Slider::new(&mut lim.retry_count, 0..=20)
                .text("Retry count")).changed();
            if changed { self.changed(); }
            if let Err(e) = self.settings.limits.validate() {
                ui.label(RichText::new(format!("Invalid limits: {e}")).color(RED));
            }
            ui.label(RichText::new(
                "Current Nexus GUI allows one transfer worker at a time. These settings do not pretend that the full install scheduler exists."
            ).size(11.0).color(GRAY));
        });
    }

    fn review_screen(&mut self, ui: &mut egui::Ui) {
        Self::panel(ui, |ui| {
            brand::section_label(ui, "05", "PLAN REVIEW", "NOT AN INSTALL BUTTON");
            ui.label(RichText::new("Your NKNOVA configuration").size(23.0)
                .strong().color(brand::TEXT));
            let (installed, download) = self.settings.variant.official_sizes_gb();
            for (key, value) in [
                ("Edition", self.settings.variant.label().to_owned()),
                ("SR Exterior Cities", if self.settings.sr_exterior_cities { "Enabled" } else { "Disabled" }.to_owned()),
                ("ENB", self.settings.enb.label().to_owned()),
                ("Published storage", format!("~{installed} GB install / ~{download} GB archives")),
                ("Skyrim directory", if self.settings.game_directory.is_empty() { "Not set".into() }
                    else { self.settings.game_directory.clone() }),
                ("New instance", self.settings.instance_directory.clone()),
                ("Archive directory", self.settings.archive_directory.clone()),
                ("Nexus speed cap", if self.settings.limits.max_download_mib_s == 0 {
                    "Unlimited".into() } else {
                    format!("{} MiB/s", self.settings.limits.max_download_mib_s)
                }),
                ("Future concurrent downloads", self.settings.limits.concurrent_downloads.to_string()),
                ("Future RAM budget", format!("{} GiB", self.settings.limits.max_ram_gib)),
            ] {
                ui.horizontal_wrapped(|ui| {
                    ui.label(RichText::new(format!("{key}:")).color(CYAN));
                    ui.label(RichText::new(value).color(brand::TEXT));
                });
            }
            ui.add_space(12.0);
            if self.settings.variant.requires_full_backend() {
                ui.label(RichText::new(
                    "This edition is currently preview/configuration only; native installation is not available."
                ).color(GOLD).strong());
            } else {
                ui.label(RichText::new(
                    "Graphics Only: read-only path and available disk capacity checks. Full native installation remains pending."
                ).color(GRAY));
            }
            ui.horizontal_wrapped(|ui| {
                if ui.add_enabled(!self.settings.variant.requires_full_backend(),
                    egui::Button::new("VERIFY GAME, PATHS & DISK SPACE")
                        .fill(GOLD).min_size(egui::vec2(195.0, 39.0))).clicked() {
                    self.preflight();
                }
                if ui.button("SAVE MY CONFIGURATION").clicked() {
                    match self.settings.save() {
                        Ok(()) => self.report("Configuration saved; no installation started.", false),
                        Err(e) => self.report(format!("Cannot save: {e}"), true),
                    }
                }
            });
            ui.label(RichText::new(
                if self.plan.is_some() { "Path preflight PASSED" } else { "Preflight not passed / not run" }
            ).color(if self.plan.is_some() { GREEN } else { GRAY }));
            if ui.add_enabled(self.plan.is_some() && !self.settings.legacy_dashboard.is_empty()
                && self.settings.variant == InstallVariant::GraphicsOnly,
                egui::Button::new("OPEN LEGACY NOLVUS INSTALLER →"))
                .clicked() {
                self.open_existing_installer();
            }
            ui.label(RichText::new(
                "Legacy wizard does not receive Rust's ENB/SREX settings automatically; reselect there. Existing Skyrim installs are untouched by this configurator."
            ).size(11.0).color(GOLD));
        });
    }
}
