//! Opt-in Nexus Mods legacy v1 connector matching the original Nolvus
//! Dashboard API client. No API keys are ever written to settings or logs.
//!
//! Nexus free accounts must supply a per-download key and expiry from a
//! user-approved NXM action; nothing here bypasses that flow.

use crate::cache::{self, Receipt};
use reqwest::blocking::Client;
use reqwest::redirect::Policy;
use reqwest::Url;
use serde::Deserialize;
use std::net::IpAddr;
use std::path::Path;
use std::time::Duration;

const API: &str = "https://api.nexusmods.com/v1/";
const USER_AGENT: &str = "NKNOVA-Rust/0.1 (Nolvus Mod Installer; Windows/Linux)";

#[derive(Debug, Clone, Deserialize)]
pub struct NexusAccount {
    #[serde(default)]
    pub name: String,
    #[serde(default)]
    pub is_premium: bool,
}

#[derive(Debug, Clone, Deserialize)]
pub struct NexusFileLink {
    #[serde(default)]
    pub name: String,
    #[serde(default)]
    pub short_name: String,
    #[serde(alias = "URI", alias = "Uri")]
    pub uri: String,
}

pub struct NxmApproval<'a> {
    pub key: &'a str,
    pub expiry: &'a str,
}

pub struct NexusV1 {
    api_key: String,
    api: Client,
    cdn: Client,
}

impl NexusV1 {
    /// Deliberately sourced from the process environment: never store,
    /// print or commit user secrets, and never send credentials to CDN hosts.
    pub fn from_environment() -> Result<Self, String> {
        let key = std::env::var("NKNOVA_NEXUS_API_KEY")
            .map_err(|_| "NKNOVA_NEXUS_API_KEY is not set (Nexus authorization required)")?;
        Self::new(key)
    }

    pub fn new(api_key: String) -> Result<Self, String> {
        if api_key.is_empty() || api_key.len() > 4096 || api_key.chars().any(char::is_control) {
            return Err("Invalid Nexus API key format".into());
        }
        let api = Client::builder()
            .user_agent(USER_AGENT)
            .connect_timeout(Duration::from_secs(15))
            .timeout(Duration::from_secs(30))
            .redirect(Policy::none())
            .build().map_err(|_| "Cannot initialize Nexus metadata HTTP client")?;
        // Download client is SEPARATE and unauthenticated by construction.
        let cdn = Client::builder()
            .user_agent(USER_AGENT)
            .connect_timeout(Duration::from_secs(15))
            .redirect(Policy::custom(|attempt| {
                if attempt.previous().len() >= 5 || require_public_https(attempt.url()).is_err() {
                    attempt.stop()
                } else {
                    attempt.follow()
                }
            }))
            .build().map_err(|_| "Cannot initialize Nexus CDN HTTP client")?;
        Ok(Self { api_key, api, cdn })
    }

    pub fn account(&self) -> Result<NexusAccount, String> {
        self.get_json("users/validate.json", None)
    }

    /// Links are provided by Nexus itself, never scraped or fabricated.
    pub fn authorized_links(
        &self, mod_id: u64, file_id: u64, nxm: Option<NxmApproval<'_>>,
    ) -> Result<Vec<NexusFileLink>, String> {
        if mod_id == 0 || file_id == 0 {
            return Err("Mod ID and file ID must be positive".into());
        }
        if let Some(ref approval) = nxm {
            if approval.key.is_empty() || approval.expiry.parse::<u64>().is_err()
                || approval.key.chars().any(char::is_control) {
                return Err("Invalid user-approved NXM key or expiry".into());
            }
        }
        let path = format!(
            "games/skyrimspecialedition/mods/{mod_id}/files/{file_id}/download_link.json"
        );
        let query = nxm.map(|x| vec![
            ("key", x.key.to_owned()),
            ("expiry", x.expiry.to_owned()),
        ]);
        let links: Vec<NexusFileLink> = self.get_json(&path, query.as_deref())?;
        if links.is_empty() { return Err("Nexus returned no authorized download links".into()); }
        for link in &links {
            let parsed = Url::parse(&link.uri).map_err(|_| "Nexus returned an invalid CDN URL")?;
            require_public_https(&parsed)?;
        }
        Ok(links)
    }

    /// Must be called only after explicit user authorization and with an
    /// independently pinned hash. No unverified file is published to cache.
    pub fn download_verified(
        &self,
        link: &NexusFileLink, id: &str, sha256: &str, archives: &Path,
        progress: impl FnMut(u64),
    ) -> Result<Receipt, String> {
        if sha256.len() != 64 || !sha256.bytes().all(|b| b.is_ascii_hexdigit()) {
            return Err("A pinned SHA-256 is required before contacting CDN".into());
        }
        let url = Url::parse(&link.uri).map_err(|_| "Invalid Nexus-supplied download URL")?;
        require_public_https(&url)?;
        let mut response = self.cdn.get(url).send()
            .map_err(|_| "Nexus CDN download request failed")?;
        if !response.status().is_success() {
            return Err(format!("Nexus CDN returned HTTP {}", response.status().as_u16()));
        }
        cache::stage_authorized_reader(id, sha256, archives, &mut response, progress)
    }

    fn get_json<T: serde::de::DeserializeOwned>(
        &self, path: &str, query: Option<&[(&str, String)]>,
    ) -> Result<T, String> {
        let url = format!("{API}{path}");
        let mut request = self.api.get(url).header("apikey", &self.api_key);
        if let Some(q) = query { request = request.query(q); }
        let response = request.send().map_err(|_| "Nexus metadata request failed")?;
        if !response.status().is_success() {
            return Err(format!("Nexus API returned HTTP {} (check account, download permissions and rate limits)",
                response.status().as_u16()));
        }
        response.json().map_err(|_| "Nexus returned unexpected JSON".to_owned())
    }
}

/// Shared for initial URL and every CDN redirect. No HTTP, local domains,
/// numeric hosts or credential-bearing URLs, even if a provider is compromised.
fn require_public_https(url: &Url) -> Result<(), String> {
    if url.scheme() != "https" || !url.username().is_empty() || url.password().is_some() {
        return Err("CDN URLs must use HTTPS without embedded credentials".into());
    }
    let host = url.host_str().ok_or("CDN link does not have a hostname")?;
    if host.parse::<IpAddr>().is_ok() || host.eq_ignore_ascii_case("localhost")
        || host.to_ascii_lowercase().ends_with(".local") {
        return Err("Local/private CDN destinations are forbidden".into());
    }
    if url.port().is_some_and(|p| p != 443) {
        return Err("Non-standard CDN HTTPS ports are forbidden".into());
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn blocks_unsafe_download_urls() {
        for text in [
            "http://cdn.nexusmods.com/test",
            "https://127.0.0.1/data",
            "https://localhost/mod.zip",
            "https://foo.local/a.zip",
            "https://username:password@cdn.example.com/file",
            "https://cdn.example.com:8443/file",
        ] {
            assert!(require_public_https(&Url::parse(text).unwrap()).is_err(), "{text}");
        }
        assert!(require_public_https(&Url::parse("https://some-cdn.nexusmods.com/mod.zip").unwrap()).is_ok());
    }

    #[test]
    fn no_key_or_invalid_nxm_authorization_is_accepted() {
        assert!(NexusV1::new(String::new()).is_err());
        let client = NexusV1::new("local-only-test-key".into()).unwrap();
        assert!(client.authorized_links(0, 1, None).is_err());
        assert!(client.authorized_links(1, 1, Some(NxmApproval { key: "x", expiry: "garbage" })).is_err());
    }
}
