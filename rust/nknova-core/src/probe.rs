//! Conservative, read-only host discovery for both Windows and Linux.

use std::path::{Path, PathBuf};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum HostOs {
    Windows,
    Linux,
    Other,
}

impl HostOs {
    pub fn current() -> Self {
        if cfg!(windows) { Self::Windows }
        else if cfg!(target_os = "linux") { Self::Linux }
        else { Self::Other }
    }

    pub fn label(self) -> &'static str {
        match self {
            Self::Windows => "WINDOWS",
            Self::Linux => "LINUX",
            Self::Other => "UNSUPPORTED OS",
        }
    }
}

/// Best-effort Steam discovery; users can always supply a non-default library.
pub fn steam_skyrim_candidates() -> Vec<PathBuf> {
    let mut roots: Vec<PathBuf> = Vec::new();
    if cfg!(windows) {
        for var in ["PROGRAMFILES(X86)", "PROGRAMFILES"] {
            if let Some(dir) = std::env::var_os(var) {
                roots.push(PathBuf::from(dir).join("Steam"));
            }
        }
    } else if let Some(home) = std::env::var_os("HOME") {
        let home = PathBuf::from(home);
        roots.extend([
            home.join(".local/share/Steam"),
            home.join(".steam/steam"),
            home.join(".steam/root"),
            home.join(".var/app/com.valvesoftware.Steam/data/Steam"),
        ]);
    }
    let mut games = Vec::new();
    for root in roots {
        add_game_from_library(&root, &mut games);
        // Discover additional Steam libraryfolders without modifying Steam config.
        let libraries = root.join("steamapps/libraryfolders.vdf");
        if let Ok(text) = std::fs::read_to_string(libraries) {
            for line in text.lines() {
                let trimmed = line.trim();
                if !trimmed.starts_with('"') || !trimmed.contains("\"path\"") {
                    continue;
                }
                let quoted: Vec<_> = trimmed.split('"').collect();
                if quoted.len() >= 4 {
                    let path = quoted[3].replace("\\\\", "\\");
                    add_game_from_library(Path::new(&path), &mut games);
                }
            }
        }
    }
    games
}

fn add_game_from_library(library: &Path, games: &mut Vec<PathBuf>) {
    let manifest = library.join("steamapps/appmanifest_489830.acf");
    if let Ok(content) = std::fs::read_to_string(manifest) {
        let name = content.lines().find_map(|line| {
            let trimmed = line.trim();
            if !trimmed.starts_with("\"installdir\"") {
                return None;
            }
            let quoted: Vec<_> = trimmed.split('"').collect();
            quoted.get(3).map(|value| value.to_string())
        });
        if let Some(name) = name {
            let candidate = library.join("steamapps/common").join(name);
            if candidate.join("SkyrimSE.exe").is_file() && !games.contains(&candidate) {
                games.push(candidate);
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn platform_has_a_classification() {
        assert!(!HostOs::current().label().is_empty());
    }
}
