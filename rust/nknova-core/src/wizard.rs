//! Nolvus v6 option catalog and resource preferences.
//! Official v6 exposes four DISTINCT variants; Graphics Only is not a
//! mode layered on top of Redux / Ultra / Ultimate.
//! See https://www.nolvus.net/appendix/installer/requirements
//! and https://www.nolvus.net/appendix/installer/install

use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, Default)]
pub enum InstallVariant {
    #[default]
    GraphicsOnly,
    Redux,
    Ultra,
    Ultimate,
}
impl InstallVariant {
    pub const ALL: [Self; 4] = [
        Self::GraphicsOnly, Self::Redux, Self::Ultra, Self::Ultimate,
    ];
    pub const fn label(self) -> &'static str {
        match self {
            Self::GraphicsOnly => "Graphics Only",
            Self::Redux => "Redux",
            Self::Ultra => "Ultra",
            Self::Ultimate => "Ultimate",
        }
    }
    pub const fn description(self) -> &'static str {
        match self {
            Self::GraphicsOnly => "Visual overhaul with a lighter, graphics-focused modlist.",
            Self::Redux => "Complete gameplay; fewer city and vegetation overhauls.",
            Self::Ultra => "Complete gameplay; 2K textures and medium city overhauls.",
            Self::Ultimate => "Complete gameplay; denser towns and 4K/2K textures.",
        }
    }
    /// Approximate v6 published install and archive requirements (GB).
    /// Informational only; never used as a filesystem capacity guarantee.
    pub const fn official_sizes_gb(self) -> (u32, u32) {
        match self {
            Self::GraphicsOnly => (235, 105),
            Self::Redux => (372, 170),
            Self::Ultra => (406, 188),
            Self::Ultimate => (426, 193),
        }
    }
    pub const fn requires_full_backend(self) -> bool {
        !matches!(self, Self::GraphicsOnly)
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(default)]
pub struct ResourceLimits {
    /// Whether future installers should prioritize background/low-impact mode.
    /// No OS priority is currently changed by this field.
    pub background_mode: bool,
    /// 0 is unlimited; enforced only by the Nexus CDN adapter when enabled.
    pub max_download_mib_s: u32,
    /// Future per-install scheduler limit. Current native Nexus UI has one worker.
    pub concurrent_downloads: u8,
    /// Future unpack/patch resource budget. No extractor is active today.
    pub max_ram_gib: u16,
    /// Future decompression/patch CPU thread budget. 0 means auto.
    pub cpu_threads: u8,
    /// Applies to local archive staging? No. Future policy only.
    pub retry_count: u8,
    /// Future job policy, currently never shuts down or suspends anything.
    pub pause_after_download: bool,
}

impl Default for ResourceLimits {
    fn default() -> Self {
        Self {
            background_mode: true,
            max_download_mib_s: 0,
            concurrent_downloads: 2,
            max_ram_gib: 8,
            cpu_threads: 0,
            retry_count: 3,
            pause_after_download: true,
        }
    }
}

impl ResourceLimits {
    pub fn validate(&self) -> Result<(), String> {
        if self.max_download_mib_s > 10_000 {
            return Err("Download cap must be 0 (unlimited) or <=10,000 MiB/s".into());
        }
        if !(1..=16).contains(&self.concurrent_downloads) {
            return Err("Concurrent download preference must be 1..=16".into());
        }
        if !(1..=256).contains(&self.max_ram_gib) {
            return Err("RAM budget must be 1..=256 GiB".into());
        }
        if self.cpu_threads > 128 {
            return Err("CPU worker preference must be 0..=128".into());
        }
        if self.retry_count > 20 {
            return Err("Retry preference must be 0..=20".into());
        }
        Ok(())
    }
    pub fn byte_rate_limit(&self) -> Option<u64> {
        (self.max_download_mib_s > 0).then(|| self.max_download_mib_s as u64 * 1024 * 1024)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn distinct_v6_variants_and_defaults() {
        assert_eq!(InstallVariant::ALL.len(), 4);
        assert!(InstallVariant::ALL.into_iter().map(InstallVariant::label)
            .collect::<std::collections::HashSet<_>>().len() == 4);
        assert_eq!(InstallVariant::GraphicsOnly.official_sizes_gb(), (235, 105));
        assert_eq!(InstallVariant::Ultimate.official_sizes_gb(), (426, 193));
    }
    #[test]
    fn preferences_validate_and_allow_unlimited() {
        let mut r = ResourceLimits::default();
        assert_eq!(r.byte_rate_limit(), None);
        assert!(r.validate().is_ok());
        r.max_download_mib_s = 5;
        assert_eq!(r.byte_rate_limit(), Some(5 * 1_048_576));
        r.concurrent_downloads = 0;
        assert!(r.validate().is_err());
        r.concurrent_downloads = 8;
        r.max_ram_gib = 0;
        assert!(r.validate().is_err());
    }
}
