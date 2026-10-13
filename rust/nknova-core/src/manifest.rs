//! Rust port of the v1 NKNOVA declarative component manifest.
//! Pure read-only parsing, dependency validation and deterministic ordering.
//! This does not download, extract or overwrite anything.

use serde::{Deserialize, Serialize};
use std::collections::{HashMap, HashSet};

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Manifest {
    pub schema_version: u32,
    pub id: String,
    pub display_name: String,
    pub game: GameSpec,
    pub runtime: RuntimeSpec,
    pub components: Vec<Component>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GameSpec {
    pub steam_app_id: u32,
    pub executable: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RuntimeSpec {
    pub proton_version: String,
    pub display_backend: String,
    pub mod_manager: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Component {
    pub id: String,
    pub version: String,
    pub source_kind: String,
    pub archive_file: Option<String>,
    pub sha256: Option<String>,
    #[serde(default)]
    pub dependencies: Vec<String>,
}

impl Manifest {
    pub fn parse(json: &str) -> Result<Self, String> {
        let manifest: Self = serde_json::from_str(json).map_err(|e| e.to_string())?;
        manifest.validate()?;
        Ok(manifest)
    }

    pub fn validate(&self) -> Result<(), String> {
        if self.schema_version != 1 {
            return Err(format!("Unsupported manifest schema {}", self.schema_version));
        }
        if !valid_id(&self.id) || self.display_name.trim().is_empty() {
            return Err("Manifest requires a safe ID and display name".into());
        }
        if self.game.steam_app_id == 0 || !safe_filename(&self.game.executable) {
            return Err("Invalid Steam game executable or app id".into());
        }
        // Windows installs run without Proton or XWayland, so the manifest
        // remains a data description; OS-specific runtime checks belong to probes.
        if self.runtime.mod_manager.trim().is_empty() {
            return Err("Missing mod manager".into());
        }
        if self.components.is_empty() {
            return Err("Manifest contains no components".into());
        }
        let mut found = HashSet::<&str>::new();
        for c in &self.components {
            if !valid_id(&c.id) || !found.insert(&c.id) {
                return Err(format!("Invalid or duplicate component ID: {}", c.id));
            }
            if c.version.trim().is_empty() {
                return Err(format!("Missing version for component {}", c.id));
            }
            match c.source_kind.as_str() {
                "upstream-dashboard" => {
                    if c.archive_file.is_some() {
                        return Err("Upstream component must not name a user archive".into());
                    }
                }
                "user-archive" => {
                    if !c.archive_file.as_deref().is_some_and(safe_filename) {
                        return Err(format!("Unsafe user archive path for {}", c.id));
                    }
                }
                other => return Err(format!("Unsupported sourceKind: {other}")),
            }
            if c.sha256.as_deref().is_some_and(|hash| {
                hash.len() != 64 || !hash.bytes().all(|b| b.is_ascii_hexdigit())
            }) {
                return Err(format!("Malformed SHA256 for {}", c.id));
            }
        }
        self.ordered().map(|_| ())
    }

    /// Dependencies before dependents. Case insensitive IDs as in the C# v1.
    pub fn ordered(&self) -> Result<Vec<&Component>, String> {
        let mut lookup = HashMap::<String, usize>::new();
        for (i, c) in self.components.iter().enumerate() {
            let key = c.id.to_ascii_lowercase();
            if lookup.insert(key, i).is_some() {
                return Err(format!("Duplicate component: {}", c.id));
            }
        }
        let mut visited = HashSet::<usize>::new();
        let mut visiting = HashSet::<usize>::new();
        let mut order = Vec::new();

        fn visit<'a>(
            index: usize,
            source: &'a [Component],
            lookup: &HashMap<String, usize>,
            visited: &mut HashSet<usize>,
            visiting: &mut HashSet<usize>,
            order: &mut Vec<&'a Component>,
        ) -> Result<(), String> {
            if visited.contains(&index) { return Ok(()); }
            if !visiting.insert(index) {
                return Err(format!("Dependency cycle at {}", source[index].id));
            }
            for dep in &source[index].dependencies {
                let key = dep.to_ascii_lowercase();
                let next = *lookup.get(&key).ok_or_else(|| {
                    format!("Unknown dependency {dep} for {}", source[index].id)
                })?;
                visit(next, source, lookup, visited, visiting, order)?;
            }
            visiting.remove(&index);
            visited.insert(index);
            order.push(&source[index]);
            Ok(())
        }

        for i in 0..self.components.len() {
            visit(i, &self.components, &lookup, &mut visited, &mut visiting, &mut order)?;
        }
        Ok(order)
    }
}

fn valid_id(value: &str) -> bool {
    !value.is_empty() &&
    value.as_bytes()[0].is_ascii_alphanumeric() &&
    value.bytes().all(|x| x.is_ascii_alphanumeric() || matches!(x, b'_' | b'.' | b'-'))
}

fn safe_filename(name: &str) -> bool {
    !name.is_empty() && name != "." && name != ".." &&
    !name.chars().any(|c| matches!(c, '/' | '\\' | ':')) &&
    !name.chars().any(char::is_control)
}

#[cfg(test)]
mod tests {
    use super::*;

    const EXAMPLE: &str = include_str!("../../../install-profiles/nknova-linux.example.json");

    #[test]
    fn ported_linux_manifest_has_deterministic_order() {
        let m = Manifest::parse(EXAMPLE).unwrap();
        let order: Vec<_> = m.ordered().unwrap().iter().map(|x| x.id.as_str()).collect();
        assert_eq!(order[0], "nolvus-awakening");
        assert!(order.iter().position(|s| *s == "community-shaders").unwrap() <
            order.iter().position(|s| *s == "cs-upscaling").unwrap());
    }

    #[test]
    fn manifest_rejects_path_traversal_duplicates_cycles() {
        let mut m = Manifest::parse(EXAMPLE).unwrap();
        m.components[1].archive_file = Some("../evil.zip".into());
        assert!(m.validate().is_err());

        let mut m = Manifest::parse(EXAMPLE).unwrap();
        m.components.push(m.components[0].clone());
        assert!(m.validate().is_err());

        let mut m = Manifest::parse(EXAMPLE).unwrap();
        m.components[0].dependencies.push("cs-upscaling".into());
        assert!(m.validate().is_err());
    }

    #[test]
    fn rejects_untrusted_hash_and_missing_dependencies() {
        let mut m = Manifest::parse(EXAMPLE).unwrap();
        m.components[1].sha256 = Some("bad".into());
        assert!(m.validate().is_err());

        let mut m = Manifest::parse(EXAMPLE).unwrap();
        m.components[0].dependencies.push("not-a-component".into());
        assert!(m.validate().is_err());
    }
}
