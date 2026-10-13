//! NKNOVA's independent cross-platform Rust installation core.
//! No GUI dependency; no code here launches games, deletes mods, or downloads archives.
//! Currently implements a safe graphics-only Nolvus plan; native API adapters follow.

pub mod cache;
pub mod manifest;
pub mod probe;
pub mod profiles;

use serde::{Deserialize, Serialize};
use std::fs;
use std::path::{Component, Path, PathBuf};

pub const SKYRIM_STEAM_APP_ID: u32 = 489830;
pub const GRAPHICS_ONLY: &str = "Graphics Only";

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, Default)]
pub enum Enb {
    #[default]
    Cabbage,
    Kauz,
    Picho,
    Amon,
}

impl Enb {
    pub const ALL: [Self; 4] = [Self::Cabbage, Self::Kauz, Self::Picho, Self::Amon];

    pub fn label(self) -> &'static str {
        match self {
            Self::Cabbage => "Cabbage ENB",
            Self::Kauz => "Kauz ENB",
            Self::Picho => "PI-CHO ENB",
            Self::Amon => "Amon Reborn",
        }
    }

    pub fn code(self) -> &'static str {
        match self {
            Self::Cabbage => "CABBAGE",
            Self::Kauz => "KAUZ",
            Self::Picho => "PICHO",
            Self::Amon => "AMON",
        }
    }
}

#[derive(Clone, Debug, Default, Serialize, Deserialize)]
#[serde(default)]
pub struct Settings {
    pub game_directory: String,
    pub instance_directory: String,
    pub archive_directory: String,
    pub legacy_dashboard: String,
    pub enb: Enb,
}

impl Settings {
    /// Persist only user-approved preferences; never store API tokens in plain text.
    pub fn load() -> Result<Self, String> {
        let path = settings_file()?;
        if !path.exists() {
            return Ok(Self::default());
        }
        let data = fs::read(&path).map_err(|e| format!("Read {}: {e}", path.display()))?;
        serde_json::from_slice(&data).map_err(|e| format!("Invalid settings: {e}"))
    }

    pub fn save(&self) -> Result<(), String> {
        let path = settings_file()?;
        let parent = path.parent().ok_or("Missing configuration directory")?;
        fs::create_dir_all(parent).map_err(|e| e.to_string())?;
        let bytes = serde_json::to_vec_pretty(self).map_err(|e| e.to_string())?;
        // Do not overwrite a symlink or partially truncate existing settings on error.
        if path.symlink_metadata().is_ok_and(|m| m.file_type().is_symlink()) {
            return Err("Configuration path is a symlink".into());
        }
        let tmp = path.with_extension("json.tmp");
        if tmp.exists() {
            return Err("Pending settings temp file: remove it after checking contents".into());
        }
        fs::write(&tmp, bytes).map_err(|e| e.to_string())?;
        if let Err(e) = fs::rename(&tmp, &path) {
            let _ = fs::remove_file(&tmp);
            return Err(format!("Save settings: {e}"));
        }
        Ok(())
    }
}

pub fn settings_file() -> Result<PathBuf, String> {
    #[cfg(windows)]
    let base = std::env::var_os("APPDATA")
        .map(PathBuf::from)
        .or_else(|| std::env::var_os("USERPROFILE").map(|h| PathBuf::from(h).join("AppData/Roaming")));
    #[cfg(not(windows))]
    let base = std::env::var_os("XDG_CONFIG_HOME")
        .filter(|x| !x.is_empty())
        .map(PathBuf::from)
        .or_else(|| std::env::var_os("HOME").map(|h| PathBuf::from(h).join(".config")));
    Ok(base.ok_or("Cannot locate user configuration directory")?
        .join("nknova").join("settings.json"))
}

#[derive(Clone, Debug)]
pub struct GraphicsOnlyPlan {
    pub game: PathBuf,
    pub target: PathBuf,
    pub archive: PathBuf,
    pub enb: Enb,
    pub variant: &'static str,
}

impl GraphicsOnlyPlan {
    /// Read-only validation. Does not create files or modify an installed game.
    pub fn prepare(settings: &Settings) -> Result<Self, String> {
        let game = checked_absolute(&settings.game_directory, "Skyrim directory")?;
        let target = checked_absolute(&settings.instance_directory, "Install directory")?;
        let archive = checked_absolute(&settings.archive_directory, "Archive directory")?;

        if !game.join("SkyrimSE.exe").is_file() {
            return Err("SkyrimSE.exe not found. Select a legitimate Steam Skyrim SE/AE folder.".into());
        }
        if !archive.is_dir() {
            return Err("Archive folder does not exist (create it outside Skyrim and the install target).".into());
        }
        if target.exists() {
            if !target.is_dir() {
                return Err("Installation target exists and is not a directory.".into());
            }
            let nonempty = fs::read_dir(&target)
                .map_err(|e| format!("Cannot inspect install directory: {e}"))?
                .next().is_some();
            if nonempty {
                return Err("Install directory is not empty; existing installs are never overwritten.".into());
            }
        }

        for (left, right) in [(&game, &target), (&game, &archive), (&target, &archive)] {
            if overlaps(left, right) {
                return Err("Game, install target and archive must not overlap.".into());
            }
        }
        for dir in [&target, &archive] {
            reject_symlink_ancestors(dir)?;
        }

        Ok(Self { game, target, archive, enb: settings.enb, variant: GRAPHICS_ONLY })
    }
}

fn checked_absolute(input: &str, name: &str) -> Result<PathBuf, String> {
    let p = Path::new(input.trim());
    if !p.is_absolute() {
        return Err(format!("{name}: choose an absolute path."));
    }
    if p.components().any(|c| matches!(c, Component::ParentDir | Component::CurDir)) {
        return Err(format!("{name}: relative components are not allowed."));
    }
    Ok(p.to_path_buf())
}

fn overlaps(left: &Path, right: &Path) -> bool {
    // On Windows Path comparisons are case sensitive even on case-insensitive volumes.
    #[cfg(windows)]
    {
        let lhs = left.to_string_lossy().to_lowercase();
        let rhs = right.to_string_lossy().to_lowercase();
        let left = Path::new(&*lhs);
        let right = Path::new(&*rhs);
        left.starts_with(right) || right.starts_with(left)
    }
    #[cfg(not(windows))]
    {
        left.starts_with(right) || right.starts_with(left)
    }
}

fn reject_symlink_ancestors(path: &Path) -> Result<(), String> {
    for dir in path.ancestors() {
        match fs::symlink_metadata(dir) {
            Ok(meta) if meta.file_type().is_symlink() =>
                return Err(format!("Refusing symlink/junction parent: {}", dir.display())),
            Ok(_) => {}
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => {}
            Err(e) => return Err(format!("Cannot inspect {}: {e}", dir.display())),
        }
    }
    Ok(())
}

pub fn check_legacy_dashboard(path: &str) -> Result<PathBuf, String> {
    let p = checked_absolute(path, "Legacy Dashboard executable")?;
    let expected = if cfg!(windows) { "NolvusDashboard.exe" } else { "NolvusDashboard" };
    if p.file_name().and_then(|s| s.to_str()) != Some(expected) {
        return Err(format!("Choose the existing {expected} executable (no arbitrary commands)."));
    }
    if !p.is_file() {
        return Err("Legacy Dashboard executable not found.".into());
    }
    Ok(p)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::{SystemTime, UNIX_EPOCH};

    fn fixture() -> (PathBuf, Settings) {
        let salt = SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_nanos();
        let root = std::env::temp_dir().join(format!("nknova-rust-{}-{salt}", std::process::id()));
        let game = root.join("steam").join("Skyrim Special Edition");
        let archives = root.join("archives");
        fs::create_dir_all(&game).unwrap();
        fs::create_dir_all(&archives).unwrap();
        fs::write(game.join("SkyrimSE.exe"), b"fixture only").unwrap();
        let cfg = Settings {
            game_directory: game.to_string_lossy().into_owned(),
            instance_directory: root.join("Nolvus GO").to_string_lossy().into_owned(),
            archive_directory: archives.to_string_lossy().into_owned(),
            legacy_dashboard: String::new(),
            enb: Enb::Cabbage,
        };
        (root, cfg)
    }

    #[test]
    fn graphics_only_plan_is_safe_and_platform_neutral() {
        let (dir, cfg) = fixture();
        let plan = GraphicsOnlyPlan::prepare(&cfg).unwrap();
        assert_eq!(plan.variant, GRAPHICS_ONLY);
        assert_eq!(plan.enb.code(), "CABBAGE");
        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn nonempty_instance_is_never_modified() {
        let (dir, cfg) = fixture();
        fs::create_dir_all(&cfg.instance_directory).unwrap();
        let marker = Path::new(&cfg.instance_directory).join("do-not-delete.txt");
        fs::write(&marker, b"save").unwrap();
        assert!(GraphicsOnlyPlan::prepare(&cfg).is_err());
        assert_eq!(fs::read(&marker).unwrap(), b"save");
        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn refuses_overlap_and_path_traversal() {
        let (dir, mut cfg) = fixture();
        cfg.instance_directory = cfg.game_directory.clone();
        assert!(GraphicsOnlyPlan::prepare(&cfg).is_err());
        cfg.instance_directory = format!("{}/../trap", dir.display());
        assert!(GraphicsOnlyPlan::prepare(&cfg).is_err());
        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn enb_codes_match_upstream() {
        let codes: Vec<_> = Enb::ALL.iter().map(|e| e.code()).collect();
        assert_eq!(codes, ["CABBAGE", "KAUZ", "PICHO", "AMON"]);
    }

    #[test]
    fn settings_roundtrip_without_tokens() {
        let cfg = Settings { enb: Enb::Picho, ..Settings::default() };
        let json = serde_json::to_string(&cfg).unwrap();
        assert!(!json.contains("token"));
        let restored: Settings = serde_json::from_str(&json).unwrap();
        assert_eq!(restored.enb, Enb::Picho);
    }
}
