mod brand;

use eframe::egui::{self, Align, Color32, RichText};
use nknova_core::cache::{self, Audit, Progress};
use nknova_core::nexus::{NexusV1, NxmApproval};
use nknova_core::probe::{self, HostOs};
use nknova_core::profiles::Profile;
use nknova_core::{Enb, GraphicsOnlyPlan, Settings};
use std::path::PathBuf;
use std::process::Command;
use std::sync::mpsc::{self, Receiver};
use std::time::Duration;

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

enum ArchiveEvent {
    Progress(Progress),
    Done(Result<usize, String>),
}

enum NexusEvent {
    Progress(u64),
    Account(Result<String, String>),
    Done(Result<String, String>),
}

struct App {
    page: Page,
    settings: Settings,
    plan: Option<GraphicsOnlyPlan>,
    status: String,
    error: bool,
    history: Vec<String>,
    archive_manifest: String,
    archive_audit: Option<Audit>,
    archive_worker: Option<Receiver<ArchiveEvent>>,
    archive_progress: String,
    nexus_mod_id: String,
    nexus_file_id: String,
    nexus_sha256: String,
    nexus_nxm_key: String,
    nexus_nxm_expiry: String,
    nexus_worker: Option<Receiver<NexusEvent>>,
    nexus_progress: String,
}

impl App {
    fn new(cc: &eframe::CreationContext<'_>) -> Self {
        brand::apply(&cc.egui_ctx);
        let (settings, status, error) = match Settings::load() {
            Ok(s) => (s, "Ready — no game files changed".to_owned(), false),
            Err(e) => (Settings::default(), format!("Settings error: {e}"), true),
        };
        // CI can capture the archive workflow without mouse/keyboard injection.
        let initial_page = if std::env::var("NKNOVA_PREVIEW_PAGE").as_deref() == Ok("archives") {
            Page::Downloads
        } else {
            Page::Installer
        };
        Self { page: initial_page, settings, plan: None, status,
               error, history: vec!["NKNOVA Rust desktop initialized".to_owned()],
               archive_manifest: String::new(), archive_audit: None,
               archive_worker: None, archive_progress: String::new(),
               nexus_mod_id: String::new(), nexus_file_id: String::new(),
               nexus_sha256: String::new(), nexus_nxm_key: String::new(),
               nexus_nxm_expiry: String::new(), nexus_worker: None,
               nexus_progress: String::new() }
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
                // Frame inherits its caller's horizontal layout. Force the
                // navigation widgets into a bounded vertical child layout.
                ui.vertical(|ui| {
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
            });
    }

    fn heading(&self, ui: &mut egui::Ui, title: &str, subtitle: &str) {
        ui.horizontal(|ui| {
            ui.vertical(|ui| {
                ui.label(RichText::new("NKNOVA  /  NATIVE").size(11.0).strong().color(CYAN));
                ui.label(RichText::new(title).size(29.0).strong().color(brand::TEXT));
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

    fn poll_archive_worker(&mut self, ui: &mut egui::Ui) {
        let events: Vec<_> = self.archive_worker.as_ref()
            .map(|rx| rx.try_iter().collect()).unwrap_or_default();
        let mut finished = false;
        for event in events {
            match event {
                ArchiveEvent::Progress(Progress::Begin { id }) =>
                    self.archive_progress = format!("Reading authorized archive: {id}"),
                ArchiveEvent::Progress(Progress::Copied { id, bytes }) =>
                    self.archive_progress = format!("SHA-256 + staging {id}: {bytes} bytes"),
                ArchiveEvent::Progress(Progress::Verified { id, bytes }) =>
                    self.archive_progress = format!("Verified {id}: {bytes} bytes"),
                ArchiveEvent::Progress(Progress::Reused { id }) =>
                    self.archive_progress = format!("Reused previously verified {id}"),
                ArchiveEvent::Progress(Progress::SkippedUpstream { id }) =>
                    self.archive_progress = format!("Upstream handled by original Nolvus: {id}"),
                ArchiveEvent::Done(result) => {
                    finished = true;
                    match result {
                        Ok(count) => self.report(format!("Archive staging complete: {count} verified objects. No Skyrim files changed."), false),
                        Err(e) => self.report(format!("Archive staging blocked: {e}"), true),
                    }
                }
            }
        }
        if finished { self.archive_worker = None; }
        else if self.archive_worker.is_some() { ui.ctx().request_repaint_after(Duration::from_millis(100)); }
    }

    fn audit_archives(&mut self) {
        let path = PathBuf::from(self.archive_manifest.trim());
        let archives = PathBuf::from(self.settings.archive_directory.trim());
        match cache::load_local_manifest(&path).and_then(|m| cache::audit(&m, &archives)) {
            Ok(report) => {
                self.report(format!("Manifest audit: {} total; {} pinned archives; {} missing hashes; {} missing files; {} upstream-only",
                    report.total, report.pinned, report.missing_hashes.len(),
                    report.missing_files.len(), report.upstream), false);
                self.archive_audit = Some(report);
            }
            Err(e) => {
                self.archive_audit = None;
                self.report(format!("Cannot audit archive manifest: {e}"), true);
            }
        }
    }

    fn stage_archives(&mut self) {
        if self.archive_worker.is_some() { return; }
        let Some(report) = self.archive_audit.as_ref() else {
            self.report("Run manifest audit before staging.", true);
            return;
        };
        if !report.missing_hashes.is_empty() || !report.missing_files.is_empty() {
            self.report("Add real SHA-256 pins and provide authorized archives before staging.", true);
            return;
        }
        let path = PathBuf::from(self.archive_manifest.trim());
        let dir = PathBuf::from(self.settings.archive_directory.trim());
        let (tx, rx) = mpsc::channel();
        self.archive_worker = Some(rx);
        self.archive_progress = "Preparing verified content-addressed cache...".into();
        std::thread::spawn(move || {
            let result = cache::load_local_manifest(&path)
                .and_then(|manifest| cache::stage_local(&manifest, &dir, |progress| {
                    let _ = tx.send(ArchiveEvent::Progress(progress));
                }))
                .map(|receipts| receipts.len());
            let _ = tx.send(ArchiveEvent::Done(result));
        });
    }

    fn poll_nexus_worker(&mut self, ui: &mut egui::Ui) {
        let events: Vec<_> = self.nexus_worker.as_ref().map(|rx|
            rx.try_iter().collect()).unwrap_or_default();
        let mut finished = false;
        for event in events {
            match event {
                NexusEvent::Progress(bytes) =>
                    self.nexus_progress = format!("TLS download + checksum: {bytes} bytes"),
                NexusEvent::Account(result) => {
                    finished = true;
                    match result {
                        Ok(message) => self.report(message, false),
                        Err(error) => self.report(format!("Nexus authorization: {error}"), true),
                    }
                }
                NexusEvent::Done(result) => {
                    finished = true;
                    match result {
                        Ok(message) => self.report(message, false),
                        Err(error) => self.report(format!("Nexus download blocked: {error}"), true),
                    }
                }
            }
        }
        if finished { self.nexus_worker = None; }
        else if self.nexus_worker.is_some() {
            ui.ctx().request_repaint_after(Duration::from_millis(100));
        }
    }

    fn verify_nexus_account(&mut self) {
        if self.nexus_worker.is_some() { return; }
        let (tx, rx) = mpsc::channel();
        self.nexus_worker = Some(rx);
        self.nexus_progress = "Checking authorized Nexus account...".into();
        std::thread::spawn(move || {
            let status = NexusV1::from_environment().and_then(|client|
                client.account().map(|account|
                    format!("Nexus connected: {} / {}",
                        account.name,
                        if account.is_premium { "Premium" } else { "Free (NXM approval required)" })
                ));
            let _ = tx.send(NexusEvent::Account(status));
        });
    }

    fn fetch_nexus_archive(&mut self) {
        if self.nexus_worker.is_some() { return; }
        let Ok(mod_id) = self.nexus_mod_id.trim().parse::<u64>() else {
            self.report("Provide a valid Nexus mod ID.", true); return;
        };
        let Ok(file_id) = self.nexus_file_id.trim().parse::<u64>() else {
            self.report("Provide a valid Nexus file ID.", true); return;
        };
        let sha = self.nexus_sha256.trim().to_owned();
        if sha.len() != 64 || !sha.bytes().all(|b| b.is_ascii_hexdigit()) {
            self.report("Provide a 64-character SHA-256 checksum before downloading.", true);
            return;
        }
        let key = std::mem::take(&mut self.nexus_nxm_key);
        let expiry = std::mem::take(&mut self.nexus_nxm_expiry);
        let dir = PathBuf::from(self.settings.archive_directory.trim());
        let (tx, rx) = mpsc::channel();
        self.nexus_worker = Some(rx);
        self.nexus_progress = "Resolving authorized Nexus download link...".into();
        std::thread::spawn(move || {
            let result = (|| -> Result<String, String> {
                let client = NexusV1::from_environment()?;
                let nxm = if key.is_empty() && expiry.is_empty() {
                    None
                } else {
                    Some(NxmApproval { key: &key, expiry: &expiry })
                };
                let links = client.authorized_links(mod_id, file_id, nxm)?;
                // Always choose the first authorized Nexus CDN option.
                let link = links.first().ok_or("Nexus did not return an approved link")?;
                let id = format!("nexus-{mod_id}-{file_id}");
                let receipt = client.download_verified(link, &id, &sha, &dir, |bytes| {
                    let _ = tx.send(NexusEvent::Progress(bytes));
                })?;
                Ok(format!("Nexus archive verified: {} bytes, SHA-256 {}. No Skyrim game files modified.",
                    receipt.bytes, receipt.sha256))
            })();
            let _ = tx.send(NexusEvent::Done(result));
        });
    }

    fn downloads(&mut self, ui: &mut egui::Ui) {
        self.heading(ui, "THE ARCHIVE", "Real Rust SHA-256 verification and non-destructive staging.");
        Self::panel(ui, |ui| {
            brand::section_label(ui, "01", "AUTHORIZED ARCHIVES", "LOCAL / IMMUTABLE CACHE");
            ui.add_space(9.0);
            ui.label(RichText::new("Archive cache directory").color(brand::TEXT).strong());
            ui.label(if self.settings.archive_directory.is_empty() {
                "Not configured — set a separate existing archive folder under Installer."
            } else { &self.settings.archive_directory });
            ui.label(RichText::new("Verified objects are written only under .nknova-verified/. Original files and the Skyrim installation are never overwritten.")
                .size(12.0).color(GRAY));
            ui.add_space(11.0);
            ui.label(RichText::new("MANIFEST JSON").monospace().color(CYAN).strong());
            if ui.add(egui::TextEdit::singleline(&mut self.archive_manifest)
                .hint_text("Absolute path to a manifest with pinned SHA-256 archives")
                .desired_width(f32::INFINITY)).changed() {
                self.archive_audit = None;
            }
            ui.horizontal(|ui| {
                if ui.add_enabled(self.archive_worker.is_none(),
                    egui::Button::new("01  /  AUDIT MANIFEST").fill(CYAN)
                        .min_size(egui::vec2(168.0, 38.0))).clicked() {
                    self.audit_archives();
                }
                let can_stage = self.archive_worker.is_none() &&
                    self.archive_audit.as_ref().is_some_and(|a|
                        a.missing_hashes.is_empty() && a.missing_files.is_empty() && a.pinned > 0);
                if ui.add_enabled(can_stage,
                    egui::Button::new(RichText::new("02  /  STAGE VERIFIED FILES").color(BACKGROUND).strong())
                        .fill(GOLD).min_size(egui::vec2(220.0, 38.0))).clicked() {
                    self.stage_archives();
                }
            });
            if let Some(report) = &self.archive_audit {
                ui.label(RichText::new(format!("MANIFEST: {} components  |  {} SHA-256 pins  |  {} upstream-only",
                    report.total, report.pinned, report.upstream)).monospace().size(11.0).color(GREEN));
                if !report.missing_hashes.is_empty() {
                    ui.label(RichText::new(format!("BLOCKED — missing checksums: {}",
                        report.missing_hashes.join(", "))).color(RED));
                }
                if !report.missing_files.is_empty() {
                    ui.label(RichText::new(format!("BLOCKED — missing local files: {}",
                        report.missing_files.join(", "))).color(RED));
                }
            }
            if self.archive_worker.is_some() {
                ui.spinner();
                ui.label(RichText::new(&self.archive_progress).monospace().color(CYAN));
            }
        });
        ui.add_space(15.0);
        Self::panel(ui, |ui| {
            brand::section_label(ui, "02", "NEXUS MODS", "AUTHORIZED / HTTPS ONLY");
            ui.add_space(9.0);
            ui.label(RichText::new("Nexus Mods account & approved downloads").size(20.0).strong().color(brand::TEXT));
            ui.label(RichText::new("Supply your API key through NKNOVA_NEXUS_API_KEY in the app environment. The key is never stored in settings or sent to a CDN.")
                .size(12.0).color(GRAY));
            if ui.add_enabled(self.nexus_worker.is_none(),
                egui::Button::new("CHECK NEXUS ACCOUNT").min_size(egui::vec2(195.0, 35.0)))
                .clicked() { self.verify_nexus_account(); }
            ui.add_space(8.0);
            ui.columns(2, |cols| {
                cols[0].label("MOD ID");
                cols[0].text_edit_singleline(&mut self.nexus_mod_id);
                cols[1].label("FILE ID");
                cols[1].text_edit_singleline(&mut self.nexus_file_id);
            });
            ui.label(RichText::new("PINNED SHA-256  /  REQUIRED").monospace().size(11.0).color(CYAN));
            ui.add(egui::TextEdit::singleline(&mut self.nexus_sha256)
                .hint_text("64 hex characters")
                .desired_width(f32::INFINITY));
            ui.label(RichText::new("FREE ACCOUNT: NXM APPROVAL KEY + EXPIRY (OPTIONAL FOR PREMIUM)")
                .monospace().size(10.0).color(GRAY));
            ui.columns(2, |cols| {
                cols[0].add(egui::TextEdit::singleline(&mut self.nexus_nxm_key)
                    .password(true).hint_text("NXM approval key"));
                cols[1].add(egui::TextEdit::singleline(&mut self.nexus_nxm_expiry)
                    .hint_text("NXM expiry Unix seconds"));
            });
            let enable = self.nexus_worker.is_none() && self.archive_worker.is_none() &&
                self.nexus_mod_id.parse::<u64>().is_ok() &&
                self.nexus_file_id.parse::<u64>().is_ok() &&
                self.nexus_sha256.trim().len() == 64;
            if ui.add_enabled(enable,
                egui::Button::new(RichText::new("DOWNLOAD VERIFIED NEXUS ARCHIVE")
                    .strong().color(BACKGROUND)).fill(GOLD).min_size(egui::vec2(300.0, 42.0)))
                .clicked() { self.fetch_nexus_archive(); }
            if self.nexus_worker.is_some() {
                ui.spinner();
                ui.label(RichText::new(&self.nexus_progress).monospace().color(CYAN));
            }
            ui.label(RichText::new("Nexus permission and approved NXM action are required. No web scraping or premium bypass; downloaded files are hash-pinned and cached, NEVER installed into Skyrim automatically.")
                .size(11.0).color(GOLD));
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
        self.poll_archive_worker(ui);
        self.poll_nexus_worker(ui);
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
