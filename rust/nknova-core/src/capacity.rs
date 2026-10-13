//! Read-only disk capacity preflight for a *new* Nolvus installation.
//! Published v6 sizes are estimates, not promises; no files are created here.

use crate::InstallVariant;
use std::fs;
use std::path::{Path, PathBuf};

/// Leave some room for filesystem bookkeeping and temporary files.
pub const HEADROOM_GB: u64 = 10;
const GB: u64 = 1_000_000_000;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SpaceReport {
    pub variant: InstallVariant,
    pub instance_required_gb: u64,
    pub archive_required_gb: u64,
    pub instance_free_gb: u64,
    pub archive_free_gb: u64,
    pub same_volume: bool,
    pub sufficient: bool,
}

impl SpaceReport {
    pub fn summary(&self) -> String {
        if self.same_volume {
            format!(
                "Same volume: ~{} GB install + ~{} GB archives + {} GB reserve; {} GB currently free",
                self.instance_required_gb, self.archive_required_gb, HEADROOM_GB,
                self.instance_free_gb
            )
        } else {
            format!(
                "Different volumes: instance ~{} GB + {} GB reserve ({} GB free); archives ~{} GB + {} GB reserve ({} GB free)",
                self.instance_required_gb, HEADROOM_GB, self.instance_free_gb,
                self.archive_required_gb, HEADROOM_GB, self.archive_free_gb
            )
        }
    }
}

/// Read capacity on the nearest existing ancestor of each target. The
/// instance directory may not exist yet and is never created by this check.
/// Must be called AFTER normal path/symlink/overlap validation.
pub fn inspect(target: &Path, archive: &Path, variant: InstallVariant) -> Result<SpaceReport, String> {
    let existing_target = existing_directory_ancestor(target)?;
    let existing_archive = existing_directory_ancestor(archive)?;
    if !archive.is_dir() {
        return Err("Archive folder must exist before disk preflight".into());
    }
    let instance_free = fs2::available_space(&existing_target)
        .map_err(|e| format!("Cannot inspect destination free space: {e}"))?;
    let archive_free = fs2::available_space(&existing_archive)
        .map_err(|e| format!("Cannot inspect archive free space: {e}"))?;
    let same_volume = same_filesystem(&existing_target, &existing_archive)?;
    let (installed, downloaded) = variant.official_sizes_gb();
    let installed = installed as u64;
    let downloaded = downloaded as u64;
    // Compare integer bytes rather than rounded-down GB figures.
    let sufficient = if same_volume {
        instance_free >= (installed + downloaded + HEADROOM_GB) * GB
    } else {
        instance_free >= (installed + HEADROOM_GB) * GB
            && archive_free >= (downloaded + HEADROOM_GB) * GB
    };
    Ok(SpaceReport {
        variant,
        instance_required_gb: installed,
        archive_required_gb: downloaded,
        instance_free_gb: instance_free / GB,
        archive_free_gb: archive_free / GB,
        same_volume,
        sufficient,
    })
}

fn existing_directory_ancestor(path: &Path) -> Result<PathBuf, String> {
    if !path.is_absolute() {
        return Err("Capacity checks require absolute directory paths".into());
    }
    let parent = path.ancestors().find(|p| p.exists())
        .ok_or("No existing filesystem ancestor for capacity check")?;
    let metadata = fs::metadata(parent).map_err(|e| format!("Inspect capacity path: {e}"))?;
    if !metadata.is_dir() {
        return Err(format!("Capacity path is not a directory: {}", parent.display()));
    }
    Ok(parent.to_path_buf())
}

#[cfg(unix)]
fn same_filesystem(a: &Path, b: &Path) -> Result<bool, String> {
    use std::os::unix::fs::MetadataExt;
    let aa = fs::metadata(a).map_err(|e| e.to_string())?;
    let bb = fs::metadata(b).map_err(|e| e.to_string())?;
    Ok(aa.dev() == bb.dev())
}

#[cfg(windows)]
fn same_filesystem(a: &Path, b: &Path) -> Result<bool, String> {
    // Windows metadata does not expose reliable volume IDs in std.
    // Comparing the absolute volume prefix correctly handles drive-letter
    // roots and UNC shares; mapped drive aliases remain a caveat.
    use std::path::Component;
    let prefix = |path: &Path| match path.components().next() {
        Some(Component::Prefix(p)) => Some(p.as_os_str().to_string_lossy().to_lowercase()),
        _ => None,
    };
    match (prefix(a), prefix(b)) {
        (Some(a), Some(b)) => Ok(a == b),
        _ => Err("Cannot establish Windows volume roots for capacity preflight".into()),
    }
}

#[cfg(not(any(unix, windows)))]
fn same_filesystem(_: &Path, _: &Path) -> Result<bool, String> {
    Err("Disk volume detection is not supported on this operating system".into())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::{SystemTime, UNIX_EPOCH};

    #[test]
    fn checks_capacity_without_creating_destination() {
        let suffix = SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_nanos();
        let root = std::env::temp_dir().join(format!("nknova-space-{}-{suffix}", std::process::id()));
        let archives = root.join("archives");
        fs::create_dir_all(&archives).unwrap();
        let target = root.join("not-yet-created").join("instance");
        let result = inspect(&target, &archives, InstallVariant::GraphicsOnly).unwrap();
        assert!(result.same_volume);
        assert_eq!(result.instance_required_gb, 235);
        assert_eq!(result.archive_required_gb, 105);
        assert!(!target.exists());
        assert!(result.summary().contains("Same volume"));
        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn requires_existing_archives_and_absolute_paths() {
        let root = std::env::temp_dir().join("nknova-space-missing");
        let r = inspect(&root, &root.join("missing"), InstallVariant::Redux);
        assert!(r.is_err());
        let r = inspect(Path::new("relative-instance"), &root, InstallVariant::Redux);
        assert!(r.is_err());
    }

    #[test]
    fn same_volume_needs_combined_budget() {
        let model = SpaceReport {
            variant: InstallVariant::GraphicsOnly,
            instance_required_gb: 235, archive_required_gb: 105,
            instance_free_gb: 300, archive_free_gb: 300,
            same_volume: true, sufficient: false
        };
        assert!(model.summary().contains("350 GB")); // 235 + 105 + 10
    }
}
