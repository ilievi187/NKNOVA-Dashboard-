//! Verified content-addressed archive cache. Never writes to the Skyrim game,
//! an existing modlist/instance or Nolvus' mutable installation paths.
//!
//! Only user-provided archives with a pinned SHA-256 from a valid manifest are
//! imported. Upstream managed components are deliberately NOT faked here.

use crate::manifest::Manifest;
use sha2::{Digest, Sha256};
use std::fs::{self, File, OpenOptions};
use std::io::{self, Read, Write};
use std::path::{Component as PathComponent, Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};

static NEXT_TEMP: AtomicU64 = AtomicU64::new(1);
const BUF: usize = 128 * 1024;
pub const CACHE_FOLDER: &str = ".nknova-verified";
const MAX_MANIFEST_BYTES: u64 = 2 * 1024 * 1024;

#[derive(Debug, Clone)]
pub struct Receipt {
    pub id: String,
    pub sha256: String,
    pub bytes: u64,
    pub path: PathBuf,
}

#[derive(Debug, Clone)]
pub enum Progress {
    Begin { id: String },
    Copied { id: String, bytes: u64 },
    Verified { id: String, bytes: u64 },
    Reused { id: String },
    SkippedUpstream { id: String },
}

#[derive(Debug, Clone)]
pub struct Audit {
    pub total: usize,
    pub pinned: usize,
    pub missing_hashes: Vec<String>,
    pub missing_files: Vec<String>,
    pub upstream: usize,
}

pub fn load_local_manifest(path: &Path) -> Result<Manifest, String> {
    if !path.is_absolute() || path.components().any(|c| matches!(c, PathComponent::ParentDir | PathComponent::CurDir)) {
        return Err("Manifest must have a normalized absolute path".into());
    }
    no_symlink_ancestors(path)?;
    let meta = fs::metadata(path).map_err(|e| format!("Cannot read manifest: {e}"))?;
    if !meta.is_file() || meta.len() > MAX_MANIFEST_BYTES {
        return Err("Manifest is not a regular JSON file or exceeds 2 MiB".into());
    }
    let text = fs::read_to_string(path).map_err(|e| format!("Cannot read UTF-8 manifest: {e}"))?;
    Manifest::parse(&text)
}

pub fn audit(manifest: &Manifest, archives: &Path) -> Result<Audit, String> {
    manifest.validate()?;
    validate_archives(archives)?;
    let mut report = Audit {
        total: manifest.components.len(), pinned: 0,
        missing_hashes: Vec::new(), missing_files: Vec::new(), upstream: 0,
    };
    for item in manifest.ordered()? {
        match item.source_kind.as_str() {
            "upstream-dashboard" => { report.upstream += 1; }
            "user-archive" => {
                let name = item.archive_file.as_deref().ok_or("Missing archive name")?;
                let source = archives.join(name);
                no_symlink_ancestors(&source)?;
                if item.sha256.is_none() { report.missing_hashes.push(item.id.clone()); }
                else { report.pinned += 1; }
                if !source.is_file() { report.missing_files.push(item.id.clone()); }
            }
            _ => return Err("Unexpected source kind".into()),
        }
    }
    Ok(report)
}

/// Import pinned, authorized archives into a stable immutable SHA-256 cache.
///
/// Does not extract, execute, move or replace the user's source files.
/// Existing cache objects are rehashed before reuse; corrupt ones are blocked.
/// Every new object is written to an exclusive partial name and published with
/// a hard-link (fails rather than replacing a concurrent/existing file).
pub fn stage_local<F>(
    manifest: &Manifest,
    archives: &Path,
    mut progress: F,
) -> Result<Vec<Receipt>, String>
where
    F: FnMut(Progress),
{
    let report = audit(manifest, archives)?;
    if !report.missing_hashes.is_empty() {
        return Err(format!(
            "SHA-256 pins required before staging: {}",
            report.missing_hashes.join(", ")
        ));
    }
    if !report.missing_files.is_empty() {
        return Err(format!(
            "Authorized source archives missing: {}",
            report.missing_files.join(", ")
        ));
    }
    let root = archives.join(CACHE_FOLDER);
    create_cache_root(&root)?;
    let mut receipts = Vec::new();
    for component in manifest.ordered()? {
        if component.source_kind == "upstream-dashboard" {
            progress(Progress::SkippedUpstream { id: component.id.clone() });
            continue;
        }
        let id = component.id.clone();
        let expected = component.sha256.as_deref().ok_or("Missing SHA-256")?.to_ascii_lowercase();
        let name = component.archive_file.as_deref().ok_or("Missing filename")?;
        let source = archives.join(name);
        no_symlink_ancestors(&source)?;
        progress(Progress::Begin { id: id.clone() });
        let destination = root.join(format!("{expected}.archive"));
        if destination.exists() {
            no_symlink_ancestors(&destination)?;
            let existing = hash_file(&destination)?;
            if existing.0 != expected {
                return Err(format!("{id}: cached object is corrupt; refusing overwrite"));
            }
            progress(Progress::Reused { id: id.clone() });
            receipts.push(Receipt { id, sha256: expected, bytes: existing.1, path: destination });
            continue;
        }
        let (bytes, destination) = stage_single_file(&source, &root, &expected, |n| {
            progress(Progress::Copied { id: id.clone(), bytes: n })
        }).map_err(|e| format!("{id}: {e}"))?;
        write_journal(&root, &id, &expected, bytes)?;
        progress(Progress::Verified { id: id.clone(), bytes });
        receipts.push(Receipt { id, sha256: expected, bytes, path: destination });
    }
    Ok(receipts)
}

fn validate_archives(archives: &Path) -> Result<(), String> {
    if !archives.is_absolute() ||
        archives.components().any(|c| matches!(c, PathComponent::ParentDir | PathComponent::CurDir))
    {
        return Err("Archive directory must be a normalized absolute path".into());
    }
    no_symlink_ancestors(archives)?;
    if !archives.is_dir() {
        return Err("Archive directory does not exist".into());
    }
    Ok(())
}

fn create_cache_root(root: &Path) -> Result<(), String> {
    no_symlink_ancestors(root)?;
    if root.exists() {
        if !root.is_dir() { return Err("Verified-cache path is not a directory".into()); }
        return Ok(());
    }
    fs::create_dir(root).map_err(|e| format!("Cannot create verified cache: {e}"))
}

fn stage_single_file<F>(
    source: &Path,
    root: &Path,
    expected: &str,
    mut progress: F,
) -> Result<(u64, PathBuf), String>
where F: FnMut(u64) {
    no_symlink_ancestors(source)?;
    let mut input = File::open(source).map_err(|e| e.to_string())?;
    let sequence = NEXT_TEMP.fetch_add(1, Ordering::Relaxed);
    let temp = root.join(format!(".partial.{}.{}", std::process::id(), sequence));
    let final_name = root.join(format!("{expected}.archive"));
    let mut output = OpenOptions::new().write(true).create_new(true).open(&temp)
        .map_err(|e| format!("Cannot acquire exclusive staging file: {e}"))?;

    let result = (|| -> Result<u64, String> {
        let mut sha = Sha256::new();
        let mut bytes = 0u64;
        let mut buffer = [0u8; BUF];
        loop {
            let got = input.read(&mut buffer).map_err(|e| e.to_string())?;
            if got == 0 { break; }
            sha.update(&buffer[..got]);
            output.write_all(&buffer[..got]).map_err(|e| e.to_string())?;
            bytes += got as u64;
            progress(bytes);
        }
        output.sync_all().map_err(|e| e.to_string())?;
        let actual = format!("{:x}", sha.finalize());
        if actual != expected {
            return Err(format!("SHA-256 mismatch: expected {expected}, got {actual}"));
        }
        if final_name.exists() {
            // Another process may have completed it concurrently; never replace.
            if hash_file(&final_name)?.0 != expected {
                return Err("Existing verified-cache object is corrupt".into());
            }
        } else {
            match fs::hard_link(&temp, &final_name) {
                Ok(()) => {}
                Err(e) if e.kind() == io::ErrorKind::AlreadyExists => {
                    if hash_file(&final_name)?.0 != expected {
                        return Err("Concurrent cache object has wrong checksum".into());
                    }
                }
                Err(e) => return Err(format!("Cannot atomically publish verified archive: {e}")),
            }
        }
        Ok(bytes)
    })();
    // Temp file is always ours and never a user's archive.
    drop(output);
    let _ = fs::remove_file(&temp);
    result.map(|bytes| (bytes, final_name))
}

pub fn hash_file(path: &Path) -> Result<(String, u64), String> {
    no_symlink_ancestors(path)?;
    let meta = fs::symlink_metadata(path).map_err(|e| e.to_string())?;
    if !meta.file_type().is_file() {
        return Err("Expected a regular archive file, not a symlink or directory".into());
    }
    let mut input = File::open(path).map_err(|e| e.to_string())?;
    let mut sha = Sha256::new();
    let mut count = 0;
    let mut buf = [0u8; BUF];
    loop {
        let got = input.read(&mut buf).map_err(|e| e.to_string())?;
        if got == 0 { break; }
        sha.update(&buf[..got]);
        count += got as u64;
    }
    Ok((format!("{:x}", sha.finalize()), count))
}

fn write_journal(root: &Path, id: &str, sha: &str, bytes: u64) -> Result<(), String> {
    let path = root.join("receipt-journal.jsonl");
    no_symlink_ancestors(&path)?;
    let mut journal = OpenOptions::new().create(true).append(true).open(path)
        .map_err(|e| format!("Cannot open receipt journal: {e}"))?;
    // Component ids are strictly validated by Manifest::validate.
    let record = serde_json::json!({
        "event": "archive_verified", "id": id,
        "sha256": sha, "bytes": bytes
    });
    writeln!(journal, "{record}").map_err(|e| e.to_string())?;
    journal.sync_data().map_err(|e| e.to_string())
}

fn no_symlink_ancestors(path: &Path) -> Result<(), String> {
    for p in path.ancestors() {
        match fs::symlink_metadata(p) {
            Ok(m) if m.file_type().is_symlink() =>
                return Err(format!("Symlinked path is not accepted: {}", p.display())),
            Ok(_) => {}
            Err(e) if e.kind() == io::ErrorKind::NotFound => {}
            Err(e) => return Err(format!("Cannot check {}: {e}", p.display())),
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::manifest::Manifest;
    use std::time::{SystemTime, UNIX_EPOCH};

    fn fixture(bytes: &[u8]) -> (PathBuf, Manifest) {
        let nonce = SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_nanos();
        let dir = std::env::temp_dir().join(format!("nknova-archive-{}-{nonce}", std::process::id()));
        fs::create_dir_all(&dir).unwrap();
        fs::write(dir.join("my-pack.zip"), bytes).unwrap();
        let (sha, _) = hash_file(&dir.join("my-pack.zip")).unwrap();
        let mut manifest = Manifest::parse(include_str!("../../../install-profiles/nknova-linux.example.json")).unwrap();
        let part = &mut manifest.components[1];
        part.archive_file = Some("my-pack.zip".into());
        part.sha256 = Some(sha);
        manifest.components.truncate(2);
        (dir, manifest)
    }

    #[test]
    fn validates_and_reuses_verified_archive_without_replacing_source() {
        let (dir, manifest) = fixture(b"archive bytes for a smoke test");
        let before = fs::read(dir.join("my-pack.zip")).unwrap();
        let result = stage_local(&manifest, &dir, |_| {}).unwrap();
        assert_eq!(result.len(), 1);
        assert_eq!(fs::read(&result[0].path).unwrap(), before);
        assert_eq!(stage_local(&manifest, &dir, |_| {}).unwrap().len(), 1);
        assert_eq!(fs::read(dir.join("my-pack.zip")).unwrap(), before);
        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn refuses_hash_mismatch_and_does_not_publish_corrupt_object() {
        let (dir, mut manifest) = fixture(b"keep originals");
        manifest.components[1].sha256 = Some("f".repeat(64));
        assert!(stage_local(&manifest, &dir, |_| {}).is_err());
        assert!(!dir.join(CACHE_FOLDER).join(format!("{}.archive", "f".repeat(64))).exists());
        assert_eq!(fs::read(dir.join("my-pack.zip")).unwrap(), b"keep originals");
        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn refuses_unpinned_or_missing_local_archives() {
        let (dir, mut manifest) = fixture(b"some data");
        manifest.components[1].sha256 = None;
        assert!(stage_local(&manifest, &dir, |_| {}).is_err());
        manifest.components[1].sha256 = Some("a".repeat(64));
        manifest.components[1].archive_file = Some("not-here.zip".into());
        assert!(stage_local(&manifest, &dir, |_| {}).is_err());
        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn refuses_corrupt_existing_object_instead_of_overwriting() {
        let (dir, manifest) = fixture(b"good");
        let root = dir.join(CACHE_FOLDER);
        fs::create_dir(&root).unwrap();
        let hash = manifest.components[1].sha256.as_ref().unwrap();
        fs::write(root.join(format!("{hash}.archive")), b"bad").unwrap();
        assert!(stage_local(&manifest, &dir, |_| {}).is_err());
        assert_eq!(fs::read(root.join(format!("{hash}.archive"))).unwrap(), b"bad");
        fs::remove_dir_all(dir).unwrap();
    }

    #[cfg(unix)]
    #[test]
    fn refuses_symlinked_archives() {
        use std::os::unix::fs::symlink;
        let (dir, mut manifest) = fixture(b"hello");
        symlink(dir.join("my-pack.zip"), dir.join("link.zip")).unwrap();
        manifest.components[1].archive_file = Some("link.zip".into());
        assert!(stage_local(&manifest, &dir, |_| {}).is_err());
        fs::remove_dir_all(dir).unwrap();
    }
}
