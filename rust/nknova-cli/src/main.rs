//! Headless NKNOVA archive operations for Hermes/Luna/SSH workflows.
//! Explicit subcommands; never installs or modifies game files.

use nknova_core::cache::{self, Progress};
use nknova_core::nexus::{NexusV1, NxmApproval};
use std::env;
use std::path::Path;

fn usage() -> &'static str {
    "NKNOVA Rust CLI
Usage:
  nknova-cli audit <absolute-manifest.json> <absolute-archive-folder>
  nknova-cli stage <absolute-manifest.json> <absolute-archive-folder> --commit-cache
  nknova-cli nexus-account
  nknova-cli nexus-download <mod-id> <file-id> <sha256> <absolute-archive-folder> --commit-cache

Set NKNOVA_NEXUS_API_KEY locally for Nexus commands.
For free-user approved Nexus manager links only, optionally set
NKNOVA_NXM_KEY and NKNOVA_NXM_EXPIRY locally. Do not supply tokens in args.

'stage' and 'nexus-download' only commit verified archives to
<archive-folder>/.nknova-verified/. No Skyrim installation is performed."
}

fn run(args: &[String]) -> Result<(), String> {
    match args {
        [command, manifest, archives] if command == "audit" => {
            let m = cache::load_local_manifest(Path::new(manifest))?;
            let report = cache::audit(&m, Path::new(archives))?;
            println!("MANIFEST: {}", m.display_name);
            println!("COMPONENTS: {} | PINNED: {} | UPSTREAM-ONLY: {}",
                report.total, report.pinned, report.upstream);
            for id in &report.missing_hashes {
                println!("BLOCKED / missing SHA-256: {id}");
            }
            for id in &report.missing_files {
                println!("BLOCKED / missing authorized local archive: {id}");
            }
            if !report.missing_hashes.is_empty() || !report.missing_files.is_empty() {
                return Err("Audit blocked: add independently verified hashes and authorized files".into());
            }
            println!("PASS: manifest archive inputs ready for staging");
        }
        [command, manifest, archives, flag]
            if command == "stage" && flag == "--commit-cache" => {
            let m = cache::load_local_manifest(Path::new(manifest))?;
            let report = cache::audit(&m, Path::new(archives))?;
            if report.pinned == 0 {
                return Err("Nothing is pinned; refusing an empty staging operation".into());
            }
            let receipts = cache::stage_local(&m, Path::new(archives), |p| match p {
                Progress::Verified { id, bytes } =>
                    println!("VERIFIED / {id}: {bytes} bytes"),
                Progress::Reused { id } => println!("REUSED / {id}"),
                Progress::SkippedUpstream { id } => println!("UPSTREAM / {id}: not installed by this tool"),
                Progress::Begin { id } => println!("CHECKING / {id}"),
                Progress::Copied { .. } => {}
            })?;
            println!("SUCCESS / {} verified archive object(s) in immutable cache", receipts.len());
        }
        [command] if command == "nexus-account" => {
            let client = NexusV1::from_environment()?;
            let account = client.account()?;
            println!("Nexus account: {} / {}",
                account.name, if account.is_premium { "Premium" } else { "Free" });
        }
        [command, mod_id, file_id, pin, archives, flag]
            if command == "nexus-download" && flag == "--commit-cache" => {
            let mod_id = mod_id.parse::<u64>().map_err(|_| "Invalid mod ID")?;
            let file_id = file_id.parse::<u64>().map_err(|_| "Invalid file ID")?;
            if pin.len() != 64 || !pin.bytes().all(|b| b.is_ascii_hexdigit()) {
                return Err("A valid SHA-256 pin is mandatory".into());
            }
            let key = env::var("NKNOVA_NXM_KEY").unwrap_or_default();
            let expiry = env::var("NKNOVA_NXM_EXPIRY").unwrap_or_default();
            let nxm = if key.is_empty() && expiry.is_empty() {
                None
            } else {
                Some(NxmApproval { key: &key, expiry: &expiry })
            };
            let client = NexusV1::from_environment()?;
            let links = client.authorized_links(mod_id, file_id, nxm)?;
            let link = links.first().ok_or("Nexus supplied no approved CDN")?;
            let id = format!("nexus-{mod_id}-{file_id}");
            println!("FETCHING / Nexus-approved archive {id}");
            let receipt = client.download_verified(link, &id, pin, Path::new(archives), |_| {})?;
            println!("VERIFIED / {} bytes / {}", receipt.bytes, receipt.sha256);
        }
        _ => return Err(usage().to_owned()),
    }
    Ok(())
}

fn main() {
    let argv: Vec<String> = env::args().skip(1).collect();
    if let Err(e) = run(&argv) {
        eprintln!("{e}");
        std::process::exit(2);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn headless_requires_explicit_commit_flag() {
        let args = vec!["stage".into(), "/tmp/m.json".into(), "/tmp/archives".into()];
        assert!(run(&args).is_err());
    }

    #[test]
    fn unsupported_commands_are_rejected() {
        assert!(run(&["delete".to_string()]).is_err());
    }
}
