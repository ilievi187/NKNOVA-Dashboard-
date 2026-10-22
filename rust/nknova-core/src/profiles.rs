//! Profile metadata is independent from the native GUI.
//! Community Shaders and AI will be implemented in distinct, non-overlapping profiles.

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Profile {
    NolvusGraphicsEnb,
    CommunityShaders,
    NknovaModsAndAi,
}

impl Profile {
    pub const ALL: [Self; 3] = [
        Self::NolvusGraphicsEnb,
        Self::CommunityShaders,
        Self::NknovaModsAndAi,
    ];

    pub fn label(self) -> &'static str {
        match self {
            Self::NolvusGraphicsEnb => "Nolvus Graphics Only + ENB",
            Self::CommunityShaders => "Community Shaders",
            Self::NknovaModsAndAi => "NKNOVA Mods + AI",
        }
    }

    pub fn implemented(self) -> bool {
        matches!(self, Self::NolvusGraphicsEnb)
    }
}

#[cfg(test)]
mod tests {
    use super::Profile;

    #[test]
    fn profile_labels_are_stable() {
        assert_eq!(
            Profile::NolvusGraphicsEnb.label(),
            "Nolvus Graphics Only + ENB"
        );
        assert_eq!(Profile::CommunityShaders.label(), "Community Shaders");
        assert_eq!(Profile::NknovaModsAndAi.label(), "NKNOVA Mods + AI");
    }

    #[test]
    fn only_nolvus_graphics_enb_is_implemented() {
        assert!(Profile::NolvusGraphicsEnb.implemented());
        assert!(!Profile::CommunityShaders.implemented());
        assert!(!Profile::NknovaModsAndAi.implemented());
    }
}
